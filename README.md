# STranslate.Plugin.Ocr.MacOcrHttp

[STranslate](https://github.com/STranslate/STranslate) 的 OCR 插件，调用运行在 Mac 上的 [mac-ocr-http](https://github.com/Fan-chou/mac-ocr-http)（Apple Vision 文字识别）。Windows 上的 STranslate 通过局域网把截图发给 Mac，返回带坐标的文本行，支持选区高亮和图片翻译。

An OCR plugin for [STranslate](https://github.com/STranslate/STranslate) that sends screenshots to a [mac-ocr-http](https://github.com/Fan-chou/mac-ocr-http) server (Apple Vision on a Mac) and returns text lines with boxes, so region highlighting and image translation work.

## 功能 / Features

- 使用原生 `POST /ocr` 接口（JSON + base64 图片），返回 `blocks → rows` 阅读顺序和行坐标框
  Uses the native `POST /ocr` endpoint (JSON with a base64 image); maps `blocks → rows` to STranslate regions/paragraphs/lines with box points
- 设置项：服务地址、Token（Bearer）、识别语言、级别（accurate/fast）、简繁转换（hans/auto）、版面（rows/document）、碎片过滤、坐标开关、绕过系统代理、超时
  Settings: base URL, token, languages, level, script, layout, fragment filter, line boxes, bypass system proxy, timeout
- 「验证」按钮：先请求 `/health`，再带 Token 请求 `/languages`
  Validate button: `/health`, then an authenticated `/languages`
- 明确的错误提示：401（Token 错误）、413（图片过大）、连接被拒绝、超时、503（服务繁忙）等
  Clear errors for 401, 413, connection refused, timeouts, 503 and more
- 未在设置中指定语言时，跟随 STranslate 的 OCR 语言（如简体中文 → `zh-Hans,en-US`，日语 → `ja-JP,en-US`）
  With no languages set, the STranslate OCR language is mapped to Vision languages

## 安装 / Install

1. 在 Mac 上安装并启动 mac-ocr-http，让它监听局域网地址（见其 README 的 `OCR_HTTP_HOST`），记下 token 文件 `~/Library/Application Support/MacOcrHttp/token` 中的值。
   Install mac-ocr-http on the Mac, listen on its LAN address (`OCR_HTTP_HOST`), and copy the value of its token file.
2. 从 [Releases](https://github.com/Fan-chou/STranslate.Plugin.Ocr.MacOcrHttp/releases) 下载 `STranslate.Plugin.Ocr.MacOcrHttp.spkg`。
   Download the `.spkg` from Releases.
3. STranslate → 设置 → 插件 → 导入/安装插件包，选择 `.spkg`，按提示重启。
   STranslate → Settings → Plugins → install the `.spkg`, then restart if asked.
4. 设置 → 服务 → OCR → 添加 `MacOcrHttp`，填写：
   Settings → Services → OCR → add `MacOcrHttp` and fill in:
   - 服务地址 / Server URL: `http://<Mac 的局域网 IP>:8741`（Mac 本机为 `http://127.0.0.1:8741`）
   - Token: 服务端 token 文件中的值 / the server token
5. 点击「验证」，看到「成功 · OK · N languages」即可使用。
   Click Validate; success shows `OK · N languages`.

## 设置说明 / Settings

| 设置 / Setting | 默认 / Default | 说明 / Notes |
|---|---|---|
| 服务地址 / Server URL | `http://127.0.0.1:8741` | 末尾误填的 `/ocr`、`/v1` 会被去掉 / a trailing `/ocr` or `/v1` is stripped |
| Token | 空 / empty | 保存方式与其他 STranslate 插件的 API Key 相同 / stored like other plugins' API keys |
| 识别语言 / Languages | 空 / empty | 例 / e.g. `zh-Hans,en-US`；空则跟随 STranslate / empty follows STranslate |
| 级别 / Level | `accurate` | `fast` 不支持中文 / `fast` has no Chinese |
| 简繁 / Script | 空 / empty | `hans` 转简体，`auto` 不转换 / `hans` converts to Simplified |
| 版面 / Layout | `rows` | `document` 需 macOS 26 / needs macOS 26 |
| 过滤碎片 / Drop fragments | 开 / on | 对应 `fragments=drop|keep` |
| 返回坐标 / Line boxes | 开 / on | 关闭后只返回纯文本 / off returns plain text only |
| 框粒度 / Box granularity | `row` | `row` 每行一个框；`line` 每个 Vision 行一个框；`block` 每段一个框（段内按行换行）/ one box per row, per Vision line, or per paragraph |
| 按空隙拆分 / Split wide gaps | 开 / on | `row` 模式下，由多个 Vision 行组成的行在大间距处拆开（如标签栏）；单个 Vision 行不拆 / splits multi-line rows at gaps ≥ 1× median line height |
| 绕过系统代理 / Bypass proxy | 开 / on | 避免代理软件拦截局域网请求 / keeps a proxy from intercepting LAN calls |
| 超时 / Timeout | 60 s | 5–600 |

## 构建 / Build

```bash
dotnet build -c Release          # 生成 / produces bin/Release/plugins/STranslate.Plugin.Ocr.MacOcrHttp.spkg
dotnet run --project tests/MockCheck   # HTTP 客户端的模拟服务器测试 / mock-server check of the HTTP client
```

在 Linux/macOS 上也能构建（`EnableWindowsTargeting`）。推送 `v*` 标签后 GitHub Actions 会构建并把 `.spkg` 附加到 Release。
Builds on Linux/macOS too (`EnableWindowsTargeting`). Pushing a `v*` tag builds the package and attaches it to a GitHub Release.

CI：`.github/workflows/release.yml` 在每次推送 main / PR 时于 Windows 上构建并运行模拟测试，上传 `.spkg` 构建产物；推送 `v*` 标签时创建 Release 并附上 `.spkg`。发布新版本：修改 `plugin.json` 的 `Version`，提交后 `git tag vX.Y.Z && git push origin vX.Y.Z`。
CI: `.github/workflows/release.yml` builds on Windows and runs the mock check on every push to main and every PR, uploading the `.spkg` as an artifact; pushing a `v*` tag creates the GitHub Release with the `.spkg` attached. To release: bump `Version` in `plugin.json`, commit, then `git tag vX.Y.Z && git push origin vX.Y.Z`.

## 说明 / Notes

- Token 以明文保存在 STranslate 的插件设置文件中（与其他插件相同）；请只在可信局域网内使用。
  The token is stored in STranslate's plugin settings like other plugins' API keys; use it on a trusted LAN.
- mac-ocr-http 的 `/health` 不需要认证；验证按钮另用 `/languages` 检查 Token。
  `/health` is unauthenticated; Validate also calls `/languages` to check the token.

## 更新日志 / Changelog

- **0.1.1**：每行一个文本框（不再按段落合并），多行之间用换行分隔；标签栏等宽间距行拆分；新增「框粒度」「按空隙拆分」设置。
  One box per row instead of per block, rows separated by newlines; wide-gap rows (tab bars) split; new granularity and split settings.
- **0.1.0**：首个版本 / first release.

## License

MIT
