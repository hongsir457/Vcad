# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Changed

- Replaced the old one-shot command path with an AgentLite `/agent/turn` tool
  loop.
- Removed the legacy shared core project, command schemas, sample command JSON,
  and old command-validation tests.
- Reworked the AutoCAD plugin into a CAD tool host for:
  - `cad.read_dwg_snapshot`
  - `cad.create_layer`
  - `cad.draw_line`
  - `cad.draw_rectangle`
  - `cad.draw_text`
- Kept the three-tab panel shape: **对话 / 配置 / 用量**.
- Updated model test connection to call the Agent turn endpoint.
- Updated documentation and bundle packaging for the Agent panel architecture.

### Security

- CAD write tools validate arguments before calling AutoCAD APIs.
- Confirm mode pauses before write tools; trusted mode can auto-execute allowed
  write tools.
- Assistant/status text is blocked from being inserted as drawing text.
- AgentLite remains loopback-only and token-protected.
