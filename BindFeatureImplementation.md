# Bind 功能实现说明

## 新语法

本次新增的数据绑定 XML 语法为：

```xml
Bind.PropertyName="SourcePropertyName"
```

例如：

```xml
<TextView Bind.Text="Title" />
<View Bind.Visible="IsVisible" />
<Slider Bind.Value="Progress" />
```

含义分别是：

- 把数据源的 `Title` 绑定到控件的 `Text`
- 把数据源的 `IsVisible` 绑定到控件的 `Visible`
- 把数据源的 `Progress` 绑定到控件的 `Value`

## 为什么采用这个语法

这次没有把绑定语法设计成属性值表达式，而是设计成属性名前缀语法，原因是：

1. 不会和普通字符串值冲突
2. 生成器更容易识别
3. 与现有 `M.` 这种特殊前缀风格一致

例如：

```xml
<TextView Text="{=Title}" />
```

这种值语法会和普通字符串冲突，因为用户可能本来就想把 `{=Title}` 当成普通文本。

而：

```xml
<TextView Bind.Text="Title" />
```

不会有这个问题。

## 生成器中的实现位置

实现主要分布在两个文件中：

- `XmlExtensions.cs`
- `ComponentGeneratorLogic.cs`

### `XmlExtensions.cs`

这里负责识别某个 XML 属性是否属于绑定语法。

判断规则很简单：

- 属性名以 `Bind.` 开头
- `Bind.` 后面必须还有目标属性名

例如：

- `Bind.Text` -> 目标属性 `Text`
- `Bind.Width` -> 目标属性 `Width`

### `ComponentGeneratorLogic.cs`

这里负责把 XML 属性转换成真正的 C# 初始化代码。

在 `GeneratePropertyAssignments(...)` 中，属性处理流程变成了：

1. 先拿到元素的所有普通属性和样式扩展属性
2. 扫描一遍，收集所有 `Bind.*` 的目标属性名
3. 再扫描一遍，分别处理绑定属性和普通属性

## 代码生成规则

### 普通属性

例如：

```xml
<TextView Text="Hello" />
```

仍然按原有流程处理，生成：

```csharp
element.Text = "Hello";
```

### 绑定属性

例如：

```xml
<TextView Bind.Text="Title" />
```

生成：

```csharp
element.Bind("Title", "Text");
```

对应关系是：

- XML 属性值 `Title` -> `Bind` 的第一个参数 `sourcePropName`
- XML 属性名中的 `Text` -> `Bind` 的第二个参数 `targetPropName`

这与运行时 API 完全对应：

```csharp
UIView.Bind(string sourcePropName, string targetPropName)
```

## 绑定与普通赋值同时存在时的规则

当前实现按需求采用“绑定优先”规则。

例如：

```xml
<TextView Text="Hello" Bind.Text="Title" />
```

最终只会生成：

```csharp
element.Bind("Title", "Text");
```

不会再生成：

```csharp
element.Text = "Hello";
```

这是为了避免同一属性同时被静态赋值和动态绑定，造成语义冲突。

## 当前实现的边界

这次实现只做了功能接入，没有加入额外的错误诊断。

也就是说：

- 如果 `Bind.Text` 对应的目标属性不存在，当前逻辑会直接跳过
- 如果源属性名写错，编译期不会报错，实际行为取决于运行时绑定逻辑
- 如果同时写了普通属性和绑定属性，当前直接忽略普通属性

这符合这次“先实现功能，不处理错误”的目标。

## 最终行为总结

支持的最小能力已经具备：

```xml
<TextView Bind.Text="Title" />
```

生成：

```csharp
element.Bind("Title", "Text");
```

并且能够与原有普通属性赋值流程共存，不会影响没有使用 `Bind.*` 的 XML。 
