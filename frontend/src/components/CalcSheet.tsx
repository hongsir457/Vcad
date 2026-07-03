import type { BoqItem, CalcLine } from "../types";

export function CalcLines({ lines }: { lines: CalcLine[] }) {
  return (
    <div className="calc-lines">
      {lines.map((l, i) => (
        <div key={i} className={"calc-line " + l.kind}>
          {l.kind === "deduct" ? "− " : l.kind === "subst" ? "  " : ""}
          {l.text}
        </div>
      ))}
    </div>
  );
}

/** 计算书: 完整呈现每个清单子目的手算过程。 */
export function CalcSheet({ boq }: { boq: BoqItem[] }) {
  return (
    <div className="calc-sheet">
      <h3 style={{ margin: "4px 0 14px", color: "var(--c-primary)" }}>
        工程量计算书
        <span style={{ fontSize: 12, color: "var(--c-text-muted)", fontWeight: 400, marginLeft: 10 }}>
          依据 GB50500-2013 / GB50854-2013 · 每项含规则引用、公式、代入与扣减明细
        </span>
      </h3>
      {boq.map((item) => (
        <div className="calc-item" key={item.code}>
          <div className="head">
            <span className="code">{item.code}</span>
            <span className="name">{item.name}</span>
            <span className="spec" style={{ fontSize: 12, color: "var(--c-text-muted)" }}>
              {item.spec.join("；")}
            </span>
            <span className="qty">
              {item.qty.toFixed(2)} {item.unit}
            </span>
          </div>
          <CalcLines lines={item.calc} />
        </div>
      ))}
    </div>
  );
}
