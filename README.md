# VCAD — 二维图纸自动三维建模与自动算量

上传/选择二维施工图, 系统模拟人工建模过程自动建立三维模型, 再模拟手算过程逐项列式计算工程量, 输出符合 GB50500 清单体系的工程量清单, 并内置"准确率 + 稳定性"评价体系与自动测试迭代循环。

![界面](docs/screenshot.png)

## 五大目标与实现对照

| 目标 | 实现 |
|---|---|
| 1. 类 Claude Code 网页版三列交互 | 左列: 图纸库与计量口径参数; 中列: 会话流(每个建模/算量步骤是可展开的步骤卡片, SSE 实时流式); 右列: 工作区(二维图纸 / 三维模型 / 工程量清单 / 计算书 / 评测 五个页签) |
| 2. 拆解建模步骤, 模拟人工建模 | 建模流水线 9 步: 读图识图 → 建立轴网 → 楼层标高 → 布置柱 → 布置梁(净长算至柱侧面) → 布置墙(净高至梁底) → 开洞放门窗 → 布置板 → 模型自检(悬空/重叠/洞口越界) |
| 3. 拆解算量步骤, 模拟手算 | 算量流水线 7 步: 确定规范与计量口径 → 列项 → 混凝土构件逐项列式 → 砌体墙扣减 → 门窗与措施项目 → 汇总取舍编清单 → 双算复核; 每个子目生成完整计算书(规则引用/公式/代入/扣减/汇总) |
| 4. 输出符合图纸表达体系与清单规范 | 识图遵循施工图表达习惯(KZ1 400x400、KL1 250x500、M1021/C1815 门窗编号、h=120 板厚标注); 输出按 GB50500-2013 十二位清单编码、项目特征、计量单位与有效位数规则(m³/m² 两位小数、樘取整), 支持导出 CSV 清单 |
| 5. 准确率稳定性评价体系 + 自动迭代生长 | 以人工手算基准(GT)逐条比对: 准确率(偏差≤0.5%)/召回率(漏项)/精确率(多项)/稳定性(实体乱序+坐标抖动扰动重跑 5 轮须一致); 迭代循环对计量口径参数做爬山寻优(含二阶变异跳出局部最优), 从朴素基线 81.2 分自动生长收敛至 100 分 |

## 快速开始

```bash
# 后端 (Python 3.11+)
cd backend
pip install -r requirements.txt
uvicorn app.main:app --port 8000

# 前端 (Node 20+, pnpm)
cd frontend
pnpm install
pnpm build        # 构建后由后端 8000 端口直接托管
# 或开发模式: pnpm dev  (5173 端口, /api 代理到 8000)
```

打开 `http://localhost:8000`, 选择图纸, 点击「对当前图纸自动建模并计算工程量」。

## 命令行

```bash
cd backend
python -m pytest tests/            # 全链路单元测试(含与手算基准核对)
python -m app.eval.benchmark       # 运行基准评测
python -m app.eval.loop            # 自动迭代调参
python -m app.eval.loop --from-scratch   # 从朴素基线演示自动生长
```

## 目录结构

```
backend/
  app/
    core/
      drawing.py      # 二维图纸数据模型 + 识图(图层/标注解析, 坐标吸附)
      geometry.py     # 三维实体建模(净长裁剪/洞口切分/多边形拉伸) + 模型自检
      qto.py          # GB50500 清单规则库 + 手算列式 + 双算复核
    pipeline/
      orchestrator.py # 建模/算量步骤链编排, SSE 事件协议
    eval/
      benchmark.py    # 准确率/召回/精确率/稳定性评测
      loop.py         # 自动测试-迭代循环(爬山 + 二阶变异)
      cases/*.gt.json # 人工手算基准答案
    data/samples/     # 样例图纸(模拟 DWG 解析后的分层实体流)
    main.py           # FastAPI 服务
  tests/
frontend/
  src/
    App.tsx                    # 三列布局与状态编排
    components/
      Sidebar.tsx              # 左列: 图纸库/参数
      ChatPanel.tsx            # 中列: 会话与步骤卡片流
      Workspace.tsx            # 右列: 五页签工作区
      Drawing2D.tsx            # SVG 施工图渲染
      Viewer3D.tsx             # three.js 三维模型
      BoqTable.tsx CalcSheet.tsx EvalPanel.tsx
docs/ARCHITECTURE.md  # 架构与扩展设计
```

## 设计说明

- **算量基于三维模型**: 净长/净高/扣减关系在建模阶段确定并记录于构件参数, 算量阶段直接引用, 保证"模型即证据"。
- **计算书可复核**: 每条清单量都能追溯到 规则引用 → 公式 → 逐构件代入 → 扣减明细 → 汇总取舍, 与造价员手算书同构。
- **评测防过拟合**: 基准答案为人工独立手算(见 `eval/cases/*.gt.json` 的 note 字段), 不由系统自身生成; 稳定性用亚容差扰动模拟不同绘图人差异。
- **可扩展输入**: `data/samples` 的实体流格式即 DWG/PDF 识别层的目标产物, 接入真实 CAD 解析(如 ezdxf / 多模态识图)只需产出同构 JSON。

更多细节见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。
