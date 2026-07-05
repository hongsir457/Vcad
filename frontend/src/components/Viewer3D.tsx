import { useEffect, useRef } from "react";
import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import type { BuildingModel } from "../types";

/** 构件配色(工业蓝灰 + 铜色门 + 玻璃蓝窗) */
const CATEGORY_STYLE: Record<string, { color: number; opacity?: number }> = {
  column: { color: 0x8fa3b0 },
  beam: { color: 0xa8b8c2 },
  slab: { color: 0x738594, opacity: 0.92 },
  wall: { color: 0xcfc3ad },
  door: { color: 0xb56b2f },
  window: { color: 0x7fb3d3, opacity: 0.55 },
  steel_column: { color: 0x35597a },
  steel_beam: { color: 0x5d87a8 },
  pipe: { color: 0x1d7a57 },
  duct: { color: 0x8a63b8, opacity: 0.85 },
  tray: { color: 0xc2891d },
  device_valve: { color: 0x1d7a57 },
  device_fixture: { color: 0xe8eef2 },
  device_air_terminal: { color: 0x8a63b8 },
  device_luminaire: { color: 0xf2e9c9 },
  device_switch: { color: 0xc2891d },
  device_socket: { color: 0xc2891d },
  rcol_core: { color: 0x8fa3b0 },
  rcol_jacket: { color: 0xb56b2f, opacity: 0.85 },
  rcol_steel: { color: 0x35597a },
};

/** 排水管道用棕色区分 */
const PIPE_SYSTEM_COLOR: Record<string, number> = {
  给水: 0x1d7a57,
  排水: 0x8a6d3b,
  消防: 0xb33d34,
};

export const CATEGORY_LEGEND: [string, string, string][] = [
  ["column", "#8fa3b0", "柱"],
  ["beam", "#a8b8c2", "梁"],
  ["wall", "#cfc3ad", "墙"],
  ["steel_column", "#35597a", "钢柱"],
  ["steel_beam", "#5d87a8", "钢梁"],
  ["pipe", "#1d7a57", "给水管"],
  ["pipe_drain", "#8a6d3b", "排水管"],
  ["duct", "#8a63b8", "风管"],
  ["tray", "#c2891d", "桥架"],
  ["device_luminaire", "#f2e9c9", "灯具"],
];

export function Viewer3D({ model }: { model: BuildingModel }) {
  const mountRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const mount = mountRef.current;
    if (!mount) return;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x16212b);

    const camera = new THREE.PerspectiveCamera(
      50, mount.clientWidth / Math.max(mount.clientHeight, 1), 0.05, 500);
    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setPixelRatio(window.devicePixelRatio);
    renderer.setSize(mount.clientWidth, mount.clientHeight);
    mount.appendChild(renderer.domElement);

    scene.add(new THREE.AmbientLight(0xffffff, 0.85));
    const sun = new THREE.DirectionalLight(0xffffff, 1.4);
    sun.position.set(8, 14, 10);
    scene.add(sun);

    // 数据坐标 z 向上 -> three.js y 向上
    const root = new THREE.Group();
    root.rotation.x = -Math.PI / 2;
    scene.add(root);

    const edgeMat = new THREE.LineBasicMaterial({ color: 0x0f1820 });

    const matCache = new Map<string, THREE.MeshLambertMaterial>();
    const matFor = (cat: string) => {
      let m = matCache.get(cat);
      if (!m) {
        const style = CATEGORY_STYLE[cat] ?? { color: 0x999999 };
        m = new THREE.MeshLambertMaterial({
          color: style.color,
          transparent: style.opacity !== undefined,
          opacity: style.opacity ?? 1,
        });
        matCache.set(cat, m);
      }
      return m;
    };

    for (const el of model.elements) {
      for (const p of el.primitives) {
        // 图元可携带比构件更细的类别(如加固柱的芯/围套/角钢)
        const mat = matFor(p.category ?? el.category);
        let mesh: THREE.Mesh;
        if (p.kind === "cylinder" && p.p1 && p.p2 && p.r) {
          const a = new THREE.Vector3(...p.p1);
          const b = new THREE.Vector3(...p.p2);
          const dir = b.clone().sub(a);
          const len = dir.length();
          if (len < 1e-6) continue;
          const geo = new THREE.CylinderGeometry(p.r, p.r, len, 14);
          const sysColor = p.system ? PIPE_SYSTEM_COLOR[p.system] : undefined;
          const cylMat = sysColor !== undefined
            ? new THREE.MeshLambertMaterial({ color: sysColor })
            : mat;
          mesh = new THREE.Mesh(geo, cylMat);
          mesh.position.copy(a.clone().add(b).multiplyScalar(0.5));
          mesh.quaternion.setFromUnitVectors(
            new THREE.Vector3(0, 1, 0), dir.normalize());
          root.add(mesh);
          continue; // 圆柱不加线框
        }
        if (p.kind === "box" && p.center && p.size) {
          const geo = new THREE.BoxGeometry(p.size[0], p.size[1], p.size[2]);
          mesh = new THREE.Mesh(geo, mat);
          mesh.position.set(p.center[0], p.center[1], p.center[2]);
          mesh.rotation.z = p.rot ?? 0;
        } else if (p.kind === "extrude" && p.points && p.z1 !== undefined) {
          const shape = new THREE.Shape(p.points.map(([x, y]) => new THREE.Vector2(x, y)));
          const geo = new THREE.ExtrudeGeometry(shape, {
            depth: p.z1 - (p.z0 ?? 0), bevelEnabled: false,
          });
          mesh = new THREE.Mesh(geo, mat);
          mesh.position.z = p.z0 ?? 0;
        } else {
          continue;
        }
        root.add(mesh);
        const edges = new THREE.LineSegments(
          new THREE.EdgesGeometry(mesh.geometry), edgeMat);
        edges.position.copy(mesh.position);
        edges.rotation.copy(mesh.rotation);
        root.add(edges);
      }
    }

    // 相机取景: 包围盒适配
    const bbox = new THREE.Box3().setFromObject(root);
    const center = bbox.getCenter(new THREE.Vector3());
    const size = bbox.getSize(new THREE.Vector3());
    const maxDim = Math.max(size.x, size.y, size.z, 1);

    const grid = new THREE.GridHelper(Math.ceil(maxDim * 2.4), 24, 0x2e4356, 0x223240);
    grid.position.y = bbox.min.y - 0.01;
    grid.position.x = center.x;
    grid.position.z = center.z;
    scene.add(grid);

    camera.position.set(center.x + maxDim * 0.95, center.y + maxDim * 0.75,
      center.z + maxDim * 0.95);
    const controls = new OrbitControls(camera, renderer.domElement);
    controls.target.copy(center);
    controls.enableDamping = true;

    let alive = true;
    const animate = () => {
      if (!alive) return;
      requestAnimationFrame(animate);
      controls.update();
      renderer.render(scene, camera);
    };
    animate();

    const onResize = () => {
      camera.aspect = mount.clientWidth / Math.max(mount.clientHeight, 1);
      camera.updateProjectionMatrix();
      renderer.setSize(mount.clientWidth, mount.clientHeight);
    };
    const ro = new ResizeObserver(onResize);
    ro.observe(mount);

    return () => {
      alive = false;
      ro.disconnect();
      controls.dispose();
      renderer.dispose();
      root.traverse((o) => {
        if (o instanceof THREE.Mesh || o instanceof THREE.LineSegments) {
          o.geometry.dispose();
        }
      });
      mount.removeChild(renderer.domElement);
    };
  }, [model]);

  return <div ref={mountRef} style={{ width: "100%", height: "100%" }} />;
}
