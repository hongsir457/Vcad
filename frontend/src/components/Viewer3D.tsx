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
};

export const CATEGORY_LEGEND: [string, string, string][] = [
  ["column", "#8fa3b0", "柱"],
  ["beam", "#a8b8c2", "梁"],
  ["slab", "#738594", "板"],
  ["wall", "#cfc3ad", "墙"],
  ["door", "#b56b2f", "门"],
  ["window", "#7fb3d3", "窗"],
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

    for (const el of model.elements) {
      const style = CATEGORY_STYLE[el.category] ?? { color: 0x999999 };
      const mat = new THREE.MeshLambertMaterial({
        color: style.color,
        transparent: style.opacity !== undefined,
        opacity: style.opacity ?? 1,
      });
      for (const p of el.primitives) {
        let mesh: THREE.Mesh;
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
