# SilkyUI Analyzer

SilkyUIFramework 项目分析与代码生成器\
可以将 UI 的 XML 结构描述转换为 C# 代码

## 特殊属性

### Class 属性

在根元素使用，填写 C# 类的全限定名称。

### Name 属性

会在类中创建一个类的属性，然后将此XML元素映射到类的属性。
随后你可以通过此属性操作 UI 元素