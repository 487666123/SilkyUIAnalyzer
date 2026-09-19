# SilkyUI 框架分析器

SilkyUI Framework 项目分析与代码生成器\
可以将 UI 的 XML 结构描述转换为 C# 代码

![预览](./code.png)

## XML 文件

UI 文件使用 `.sui.xml` 后缀，并作为 `AdditionalFiles` 提供给生成器。
在根元素声明 `xmlns:sui="https://github.com/487666123/SilkyUIFramework"`；需要绑定时再声明 `xmlns:bind="https://github.com/487666123/SilkyUIFramework/Binding"`，
通过 `sui:Class` 指定实现 `IContainer<T>` 的 C# 类的全限定名称；`UIElementGroup` 及其派生类已经满足此条件。
`sui` 和 `bind` 前缀都可以替换，命名空间 URI 必须保持一致。

## CLR XML 命名空间

无命名空间的标签继续通过 `XmlElementMappingAttribute` 查找别名。也可以声明 CLR 命名空间，直接使用真实的类名，不要求类标注映射特性：

```xml
<Body xmlns:sui="https://github.com/487666123/SilkyUIFramework"
      xmlns:local="clr-namespace:MyMod.Elements"
      xmlns:ext="clr-namespace:OtherMod.Controls"
      sui:Class="MyMod.MainPanel">
    <TextView Text="已有别名" />
    <local:StatusPanel sui:Name="Status" />
    <ext:ProgressIndicator />
</Body>
```

- 语法为 `clr-namespace:Namespace`，区分大小写，声明内不允许空白或参数；不再接受 `;assembly=...`。命名空间各段为 CLR 标识符，不使用 C# 的 `@` 转义；命名空间为空表示全局命名空间。
- `<ext:ProgressIndicator />` 按 C# 的 `global::OtherMod.Controls.ProgressIndicator` 解析。查找范围包括当前项目，以及通过 `global` 引用别名可见的依赖程序集；普通项目引用默认在此范围内。不会搜索磁盘上的 DLL 或自动添加引用，仅通过其他 `extern alias` 暴露的类型不在此范围内。
- 同名类型遵循 C# 的绑定规则：当前项目的源码类型优先于引用程序集中的同名类型；多个引用中的同名类型无法消除歧义时报告 `SUI004`。`global::` 不会绕过类型访问限制，也不受普通 `using` 别名影响。
- CLR 标签只支持当前生成位置可访问的顶层、非泛型、非抽象、非静态类（包括符合条件的 record class），并要求可访问的零参数构造函数。带可选参数的构造函数不等同于零参数构造函数。文件局部类型不受支持；类型绑定以根类的一个带类体的源码声明为上下文，若同文件的 `file` 类型遮蔽了其他同名类型，将报告错误，不会回退到其他文件或程序集中的同名类型。
- 类型或基类包含 `required` 成员时，零参数构造函数必须标注 `SetsRequiredMembers`，因为当前生成方式在对象创建后逐项赋属性。
- 类型解析使用 XML 命名空间 URI，前缀名称任意；支持默认命名空间及嵌套作用域中的声明覆盖。显式 CLR 查找失败时不会回退到同名别名。
- `sui:Name` 属性声明与对象创建使用同一个解析结果；对于可访问的 internal CLR 类型，生成的命名属性也是 internal，避免公开不可公开的类型。
- 根对象仍由 `sui:Class` 绑定，并沿用原有容器接口要求；根标签本身不会创建新对象。`prop:*` 属性节点使用独立的 Properties 命名空间展开已有属性。
- 声明格式错误（包括旧的程序集参数）、类型查找有歧义、类型不可用或无法创建，以及普通元素使用未知命名空间，都会报告带 XML 文件位置的生成错误。

此功能只扩展类型查找，不增加字典、基础类型直接值、泛型标签或新的构造语法。

## 特殊元素

### sui:Style 元素

定义通用样式，`sui:Style` 元素的 `sui:Name` 属性为此样式的名称。
样式属性直接写在该元素上；无前缀属性（包括 `Name`）都会作为样式属性参与赋值。
样式名称和普通样式属性职责分离，避免与控件的 `Name` 属性冲突。

```xml
<Body xmlns:sui="https://github.com/487666123/SilkyUIFramework"
      sui:Class="MyMod.MyPanel"
      sui:Style="BasePanel">
    <sui:Style sui:Name="BasePanel" Name="PanelName" Width="300" Height="200" />
</Body>
```

示例中的 `MyMod.MyPanel` 需要替换为实际组件类名，样式属性需要对应目标控件的可写属性。
遇到重复 `sui:Name` 时以首个样式定义为准。
元素通过 `sui:Style="BasePanel"` 引用样式，多个样式名称用空格隔开。
同名静态属性以后面的样式为准，元素直接声明的静态属性覆盖样式中的静态属性。
`bind:*` 绑定优先于同名静态属性赋值；绑定命名空间 URI 是 `https://github.com/487666123/SilkyUIFramework/Binding`。
不再使用 `<Text Value="Hello" />` 这样的子元素定义样式属性。

## 特殊属性

### sui:Class 属性

在根元素使用，填写 C# 类的全限定名称。

### sui:Name 属性

会在类中创建一个类的属性，然后将此XML元素映射到类的属性。
随后你可以通过此属性操作 UI 元素

### bind:* 属性

`bind:Text="Title"` 把数据源的 `Title` 绑定到控件的 `Text` 属性。`bind:` 前缀可以替换，必须绑定到 Binding URI；绑定属性名需要手写或使用 VSIX 补全。

```xml
<Body xmlns:sui="https://github.com/487666123/SilkyUIFramework"
      xmlns:bind="https://github.com/487666123/SilkyUIFramework/Binding"
      sui:Class="MyMod.MyPanel">
    <Panel bind:Text="Title" />
</Body>
```

### prop:* 属性展开元素

声明 `xmlns:prop="https://github.com/487666123/SilkyUIFramework/Properties"` 后，`<prop:Container>` 表示访问父对象已有的 `Container` 属性。它不会创建新对象，也不会把该属性对象作为新子控件添加到父容器。

```xml
<Body xmlns:sui="https://github.com/487666123/SilkyUIFramework"
      xmlns:prop="https://github.com/487666123/SilkyUIFramework/Properties"
      sui:Class="MyMod.MyPanel">
    <Panel>
        <prop:Container FlexDirection="Column" />
    </Panel>
</Body>
```

示例要求父对象已有可读取且已初始化的对应属性。需要进一步展开时嵌套属性元素，例如在 `<prop:Appearance>` 中写 `<prop:Border Width="2" />`，表示设置 `父对象.Appearance.Border.Width`；不支持把多层属性路径写进一个标签名。

`prop` 只是推荐前缀：实际按 Properties URI 识别，可改用其他前缀，也支持默认命名空间和嵌套覆盖。默认 Properties 命名空间内的无前缀元素都是属性节点；需要写普通控件别名时用 `xmlns=""` 清除默认命名空间，或使用显式 CLR 前缀。普通无前缀属性仍用于配置当前对象。
