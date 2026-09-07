# 旧版 XML 迁移指南

本文用于把旧版 SilkyUI XML 迁移到当前 `.sui.xml` 语法。

## 1. 修改文件后缀

生成器现在只处理 `.sui.xml` 文件。

```text
旧：UserPanel.xml
新：UserPanel.sui.xml
```

同时检查项目文件中的 `AdditionalFiles` 配置，确保迁移后的文件仍会被作为附加文件传给 Analyzer。

## 2. 声明 SilkyUI 命名空间

在根元素上声明 SUI 命名空间：

```xml
xmlns:sui="https://github.com/487666123/SilkyUIFramework"
```

前缀不必叫 `sui`，但 URI 必须完全一致。

## 3. 迁移根元素的 Class

```xml
<!-- 旧版 -->
<Body Class="MyMod.Views.MainPanel">

<!-- 当前版 -->
<Body xmlns:sui="https://github.com/487666123/SilkyUIFramework"
      sui:Class="MyMod.Views.MainPanel">
```

`Body` 仍然是根元素，`sui:Class` 的值仍然填写继承 `UIElementGroup` 的 C# 类型全名。

## 4. 迁移元素 Name

把元素上的普通 `Name` 改为 `sui:Name`：

```xml
<!-- 旧版 -->
<TextView Name="Title" />

<!-- 当前版 -->
<TextView sui:Name="Title" />
```

使用 `sui:Name` 后，生成器会生成对应的 C# 控件属性。普通控件属性中的 `Name` 不再承担这个 XML 映射职责。

## 5. 迁移样式

### 样式定义

旧版通常把样式写成 `Style.*` 元素或使用无命名空间的样式元素。当前版统一使用 SUI 命名空间：

```xml
<!-- 旧版示意 -->
<Style.BasePanel Width="300" Height="200" />

<!-- 当前版 -->
<sui:Style sui:Name="BasePanel" Width="300" Height="200" />
```

### 样式引用

```xml
<!-- 旧版 -->
<Panel Style="BasePanel" />

<!-- 当前版 -->
<Panel xmlns:sui="https://github.com/487666123/SilkyUIFramework"
       sui:Style="BasePanel" />
```

多个样式名称仍然用空格分隔：

`sui:Style="BasePanel CompactPanel"`

样式属性直接写在 `sui:Style` 元素上；`sui:Name` 只负责样式名称。重复样式名以第一次定义为准。

## 6. 迁移数据绑定

把旧版 `Bind.Property="SourceProperty"` 改为 Binding 命名空间属性：

```xml
<!-- 旧版 -->
<TextView Bind.Text="Title" />

<!-- 当前版 -->
<Body xmlns:sui="https://github.com/487666123/SilkyUIFramework"
      xmlns:bind="https://github.com/487666123/SilkyUIFramework/Binding"
      sui:Class="MyMod.Views.MainPanel">
    <TextView bind:Text="Title" />
</Body>
```

含义不变：源属性 `Title` 绑定到控件的 `Text` 属性。

`bind` 前缀可以替换，但必须绑定到以下 URI：

```text
https://github.com/487666123/SilkyUIFramework/Binding
```

绑定优先于同名静态属性：

```xml
<TextView Text="备用文本" bind:Text="Title" />
```

生成器只为 `Text` 生成绑定调用，不再生成静态赋值。

## 7. M.* 不需要迁移

`M.*` 是成员属性语法，不属于本次命名空间迁移范围，继续原样使用：

```xml
<M.Mask />
<M.Container FlexDirection="Column" />
```

不要把它改成 `M:Mask` 或 `m:Mask`。未来如果设计新的 `m:` 语法，会另行提供迁移说明。

## 8. 完整示例

```xml
<?xml version="1.0" encoding="utf-8"?>
<Body xmlns:sui="https://github.com/487666123/SilkyUIFramework"
      xmlns:bind="https://github.com/487666123/SilkyUIFramework/Binding"
      sui:Class="MyMod.Views.MainPanel">
    <sui:Style sui:Name="BasePanel" Width="300" Height="200" />

    <TextView sui:Name="Title" bind:Text="Title" />
    <Panel sui:Style="BasePanel">
        <M.Container FlexDirection="Column" />
    </Panel>
</Body>
```

## 9. 迁移检查清单

- 文件名已从 `.xml` 改为 `.sui.xml`。
- 根元素声明了 SUI 命名空间。
- `Class` 已改为 `sui:Class`。
- 用于生成 C# 控件属性的 `Name` 已改为 `sui:Name`。
- `Style` 已改为 `sui:Style`，样式定义已改为 `sui:Style` 元素。
- `Bind.*` 已改为 `bind:*`，并声明 Binding URI。
- `M.*` 保持原样。
- 源属性名仍需根据运行时绑定对象确认，Analyzer 不会验证源属性是否存在。
