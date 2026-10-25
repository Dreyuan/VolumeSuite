第一次用github，软件主要由deepseek网页端写的，readme也是用ai写的，我做一部分标注

# VolumeSuite (v0.8-beta)

一个基于 WPF (.NET 8) 开发的现代化、高度可扩展的 Windows 桌面工具箱启动器。

## ✨ 核心特性

- **双背景系统**：支持自定义图片背景（自带管理、缩略图切换）与系统级毛玻璃（Acrylic/BlurBehind）。【感觉这里写错了，我记得我用的是setWindowCompositionAttribute】
- **全局动态强调色**：基于 `DynamicResource`，一键替换全软件（侧边栏、开关、滑条）的皮肤颜色。
- **极致的 UI 细节**：窗口可调圆角、边缘感知吸附（拖拽拉伸）、鼠标悬浮微动效、滚动边缘渐隐。【圆角做得有点垃圾】
- **颜色选择器**：内置色相环、HEX/RGB 输入、常用色板、收藏夹，以及**全局屏幕吸管**工具。
- **数据驱动的工具接入**：只需添加文件夹和 `tool.json`，即可动态渲染全新的工具设置页，无需修改主程序。
- **单实例 + 托盘驻留**：命名管道通信，再次启动会自动唤起已驻留的窗口。

---

## 🛠️ 技术实现与架构备忘（核心记录）

### 1. 数据驱动的设置页 (Schema Driven)
- **原理**：扫描 `tools/*/tool.json`，将其反序列化为 `ToolManifest`。通过 `SettingsItemViewModel.Create()` 工厂方法，根据 `type` 动态生成对应的 ViewModel（Bool/Number/Color 等）。
- **渲染**：XAML 中利用隐式的 `DataTemplate` 自动匹配不同类型的 ViewModel 进行渲染，实现了设置项与 UI 的完全解耦。

### 2. 窗口级毛玻璃 (Acrylic)
- **避坑记录**：原使用 `Wpf.Ui` 的 `FluentWindow`，但在 Win10 下 DWM 模糊层会被强制覆盖。后抛弃 `FluentWindow`，改用普通 `Window` + `WindowChrome`。
- **实现**：底层调用未公开 API `SetWindowCompositionAttribute` (状态 `ACCENT_ENABLE_BLURBEHIND`)，并将 `CompositionTarget.BackgroundColor` 设为 `Transparent`，将 WPF 视觉树悬浮在 DWM 模糊层之上。

### 3. 窗口圆角 (Window Corner)
- **原理**：由于 Win10 没有原生圆角，使用 GDI32 的 `CreateRoundRectRgn` 和 `SetWindowRgn` 进行硬裁剪。
- **单角变直**：通过 `CombineRgn` 拼接矩形与圆角矩形，实现了鼠标靠近某个角时，该角自动变成直角，方便用户拉伸窗口。

### 4. 全局吸管取色器 (Color Picker / EyeDropper)
- **原理**：通过 `WH_MOUSE_LL` 和 `WH_KEYBOARD_LL` 设置全局低级钩子。
- **取色**：利用 `GetDC` 和 `GetPixel` 获取屏幕像素，配合 `UpdatePick` 实现实时取色预览。回车确认，Esc 取消。

### 5. 滚动边缘渐隐 (Scroll Fade)
- **当前实现**：基于 `OpacityMask` (LinearGradientBrush) 计算视口上下边缘。虽然有效，但在大范围渐变时仍有明显“透明度叠加”的痕迹。【这个不对吧，渐隐这个版本还没有，做了好久效果不行】
- **未来优化**：计划在 WinUI 3 中利用 `CompositionMaskBrush` 或 `D3D11` 实现真正的 GPU 局部高斯模糊遮罩。

---

## 🗺️ 未来计划 (Roadmap)

- [ ] **WPF v0.8-beta**：当前稳定版，修复偶发的第三方软件（MyDockFinder）窗口冲突，优化滚动渐隐边缘。【毛玻璃模式下，在MyDockFinder打开动画期间内有概率模糊失效，只剩下透明，看起来像闪一下，解决办法是去MyDockFinder设置 最小化 最小化动画截图方式 改成DXGI或GDI】
- [ ] **v1.0 (归档)**：补齐全局异常捕获，完善日志系统，清理测试文件。
- [ ] **WinUI 3 重构版**：
  - [ ] 迁移底层 Services 与 ViewModels。
  - [ ] 重写 XAML，使用 `VisualStateManager` 替代 `DataTrigger`。
  - [ ] 引入 `Composition API` 实现高性能的动态粒子特效与滚动边缘模糊。
- [ ] **“疯狂实验”**：尝试用 Unity / Unreal Engine 驱动高帧率桌面特效（仅作为探索，不保证发布）。

---

## 🚀 如何运行

1. 确保安装 .NET 8 SDK。
2. 克隆仓库并打开 `VolumeSuite.sln`。【去Releases下，这个得要自己编译，Releases下完就能用，打不开可能是没装net 8.0，几百兆的那个版本不用下net】
3. 编译运行。
4. 编写你的第一个工具：在 `tools/你的工具名/` 目录下创建 `tool.json`，参考项目内的示例。【目前就做了快捷键控制音量的工具，还没上传，一会改】



【就先这样，第一次写软件，没有编程基础，第一次用github，等后面再改】
