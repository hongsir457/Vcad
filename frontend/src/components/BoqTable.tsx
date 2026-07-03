import { Fragment, useState } from "react";
import type { BoqItem, CrossCheck } from "../types";
import { CalcLines } from "./CalcSheet";

export function BoqTable({ boq, checks }: { boq: BoqItem[]; checks: CrossCheck[] }) {
  const [openCode, setOpenCode] = useState<string | null>(null);

  return (
    <div>
      <table className="data-table">
        <thead>
          <tr>
            <th style={{ width: 36 }}>#</th>
            <th>项目编码</th>
            <th>项目名称</th>
            <th>项目特征</th>
            <th style={{ textAlign: "right" }}>工程量</th>
            <th>单位</th>
            <th>备注</th>
          </tr>
        </thead>
        <tbody>
          {boq.map((item, i) => (
            <Fragment key={item.code}>
              <tr
                className="expandable"
                onClick={() => setOpenCode(openCode === item.code ? null : item.code)}
                title="点击展开手算过程"
              >
                <td>{i + 1}</td>
                <td className="code">{item.code}</td>
                <td>{item.name}</td>
                <td className="spec">{item.spec.join("；")}</td>
                <td className="num">{item.qty.toFixed(item.unit === "樘" ? 0 : 2)}</td>
                <td>{item.unit}</td>
                <td className="spec">
                  {Object.entries(item.extra).map(([k, v]) => `${v} ${k}`).join(" ")}
                </td>
              </tr>
              {openCode === item.code && (
                <tr>
                  <td colSpan={7} style={{ background: "var(--c-surface-2)", padding: "10px 16px" }}>
                    <CalcLines lines={item.calc} />
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
        </tbody>
      </table>

      {checks.length > 0 && (
        <div style={{ padding: "14px 16px" }}>
          <div style={{ fontWeight: 600, marginBottom: 8 }}>复核 · 双算对比</div>
          <table className="data-table">
            <thead>
              <tr>
                <th>复核项</th>
                <th style={{ textAlign: "right" }}>三维几何量</th>
                <th style={{ textAlign: "right" }}>清单量</th>
                <th style={{ textAlign: "right" }}>偏差</th>
                <th>结论</th>
              </tr>
            </thead>
            <tbody>
              {checks.map((c) => (
                <tr key={c.item}>
                  <td>{c.item}</td>
                  <td className="num">{c.geometry.toFixed(3)}</td>
                  <td className="num">{c.boq.toFixed(3)}</td>
                  <td className="num">{(c.deviation * 100).toFixed(2)}%</td>
                  <td>
                    <span className={"status-chip " + (c.pass ? "ok" : "warn")}>
                      {c.pass ? "通过" : "待复核"}
                    </span>{" "}
                    <span className="spec">{c.note}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
