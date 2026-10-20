# 逐字输入 KeyboardTyper

把你准备好的文字逐字输入到网页或 Windows 应用中。先选一个版本下载：

- [下载 v1.00 · Edge / Chrome 浏览器版](https://github.com/Hitachi-Mako71/KeyboardTyper/releases/download/v1.00/KeyboardTyper-v1.00-Edge.zip)
- [下载 v2.00 · Windows 桌面版](https://github.com/Hitachi-Mako71/KeyboardTyper/releases/download/v2.00/KeyboardTyper-v2.00-Windows.zip)
- [查看全部版本和更新记录](https://github.com/Hitachi-Mako71/KeyboardTyper/releases)

## 选哪个版本？

| 你想做什么 | 推荐版本 | 说明 |
| --- | --- | --- |
| 只在网页里输入文字 | **v1.00 浏览器版** | 从浏览器扩展弹窗启动，支持 Edge 和 Chrome |
| 在网页或其他 Windows 程序里输入 | **v2.00 桌面版** | 独立运行，通过 Windows 键盘输入接口逐字输入 |

## v1.00 浏览器版：安装和使用

1. 下载并解压 ZIP。
2. 在 Edge 地址栏打开 `edge://extensions`；使用 Chrome 则打开 `chrome://extensions`。
3. 打开“开发人员模式”，选择“加载解压缩的扩展”，再选中解压出来的 **KeyboardTyper** 文件夹（里面有 `manifest.json`）。
4. 打开目标网页，点扩展图标，填写正文后点“选择输入框并开始”。
5. 在倒计时结束前点网页输入框。输入完成后检查内容，再手动提交。

按 **Esc** 或 **Ctrl + Shift + Y** 可以停止。浏览器版只对网页输入，不会模拟 Windows 系统键盘。

## v2.00 Windows 桌面版：使用

1. 下载并解压 ZIP，双击 `WindowsKeyboardTyper.exe`。
2. 填写正文。中文建议保留默认的 **Unicode 输入**；先用默认速度和倒计时即可。
3. 点“开始输入”，在倒计时结束前切换到网页或其他程序的输入框。
4. 需要中止时按 **F8** 或 **Esc**。完成后检查内容，再手动提交。

## 使用提示

- 正文会从当前光标位置开始输入；选中的文字可能被替换。重要内容先用短文本试一下。
- 换行方式由目标网页或程序决定。默认的 Shift + Enter 在部分网站可能触发发送。
- 两个版本都不会自动提交表单，也不保证适用于所有网站或输入框。

<details>
<summary>隐私与技术说明</summary>

浏览器版不读取剪贴板、不向服务器发送正文；Windows 版在本机运行，正文只在输入期间保留于内存。Windows 版使用操作系统的软件输入接口，属于模拟输入，不是实体键盘，也不保证能绕过网站的输入限制。请遵守目标网站和应用的使用规则。

</details>

## 源码

- [浏览器版源码](https://github.com/Hitachi-Mako71/KeyboardTyper/tree/main/v1.00/BrowserExtension)
- [Windows 桌面版源码](https://github.com/Hitachi-Mako71/KeyboardTyper/tree/main/v2.00/WindowsDesktop)
