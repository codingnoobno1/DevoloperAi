import 'ast.dart';
import 'expressions.dart';

/// Renders a WidgetNode tree into Flutter Dart code.
class DartRenderer {
  static String render(WidgetNode node) {
    if (node is RawNode) return node.code;

    if (node is ColumnNode) {
      final children = node.spacing != null
          ? node.children.map((c) => render(c)).join(', AppDesign.verticalSpace(${node.spacing}), ')
          : node.children.map((c) => render(c)).join(', ');

      return """
Column(
  crossAxisAlignment: ${node.crossAxisAlignment},
  mainAxisAlignment: ${node.mainAxisAlignment},
  mainAxisSize: ${node.mainAxisSize},
  children: [$children],
)""";
    }

    if (node is RowNode) {
      final children = node.spacing != null
          ? node.children.map((c) => render(c)).join(', AppDesign.horizontalSpace(${node.spacing}), ')
          : node.children.map((c) => render(c)).join(', ');

      return """
Row(
  crossAxisAlignment: ${node.crossAxisAlignment},
  mainAxisAlignment: ${node.mainAxisAlignment},
  children: [$children],
)""";
    }

    if (node is PaddingNode) {
      return "Padding(padding: ${_renderEdgeInsets(node.padding)}, child: ${render(node.child)})";
    }

    if (node is CenterNode) {
      return "Center(child: ${render(node.child)})";
    }

    if (node is ExpandedNode) {
      return "Expanded(flex: ${node.flex}, child: ${render(node.child)})";
    }

    if (node is SizedBoxNode) {
      final dims = [
        if (node.width != null) "width: ${node.width}",
        if (node.height != null) "height: ${node.height}",
        if (node.child != null) "child: ${render(node.child!)}"
      ].join(", ");
      return "SizedBox($dims)";
    }

    if (node is ContainerNode) {
      final props = [
        if (node.width != null) "width: ${node.width}",
        if (node.height != null) "height: ${node.height}",
        if (node.padding != null) "padding: ${_renderEdgeInsets(node.padding!)}",
        if (node.margin != null) "margin: ${_renderEdgeInsets(node.margin!)}",
        if (node.decoration != null) "decoration: ${node.decoration}",
        if (node.alignment != null) "alignment: ${_renderAlignment(node.alignment!)}",
        if (node.child != null) "child: ${render(node.child!)}"
      ].join(", ");
      return "Container($props)";
    }

    if (node is CardNode) {
      final elevation = node.elevation ?? 'AppDesign.spacingXs';
      final color = node.color != null ? "color: ${_renderColor(node.color!)}" : null;
      final props = ["child: ${render(node.child)}", "elevation: $elevation", if (color != null) color].join(", ");
      return "UIFactory.card($props)";
    }

    if (node is ButtonNode) {
      final child = node.fullWidth 
        ? "Container(width: double.infinity, alignment: Alignment.center, child: Text('${node.text}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16)))"
        : "Text('${node.text}')";
      return "UIFactory.button(onPressed: ${node.action}, child: $child)";
    }

    if (node is TextNode) {
      return "Text('${node.text}', style: ${node.style != null ? _renderTextStyle(node.style!) : 'null'})";
    }

    if (node is IconNode) {
      final props = <String>[
        node.icon,
        if (node.size != null) "size: ${node.size}",
        if (node.color != null) "color: ${_renderColor(node.color!)}",
      ];
      return "Icon(${props.join(', ')})";
    }

    if (node is ScaffoldNode) {
      final props = [
        if (node.appBar != null) "appBar: ${render(node.appBar!)}",
        if (node.body != null) "body: ${render(node.body!)}",
        if (node.bottomNavigationBar != null) "bottomNavigationBar: ${render(node.bottomNavigationBar!)}",
        if (node.floatingActionButton != null) "floatingActionButton: ${render(node.floatingActionButton!)}",
        if (node.backgroundColor != null) "backgroundColor: ${_renderColor(node.backgroundColor!)}",
      ].join(", ");
      return "Scaffold($props)";
    }

    if (node is SafeAreaNode) {
      return "SafeArea(child: ${render(node.child)})";
    }

    if (node is ListViewNode) {
      final children = node.children.map((c) => render(c)).join(", ");
      return """
ListView(
  padding: ${node.padding != null ? _renderEdgeInsets(node.padding!) : 'EdgeInsets.zero'},
  shrinkWrap: ${node.shrinkWrap},
  physics: ${node.physics},
  children: [$children],
)""";
    }

    if (node is GridViewNode) {
      final children = node.children.map((c) => render(c)).join(", ");
      return """
GridView.count(
  crossAxisCount: ${node.crossAxisCount},
  mainAxisSpacing: ${node.mainAxisSpacing},
  crossAxisSpacing: ${node.crossAxisSpacing},
  childAspectRatio: ${node.childAspectRatio},
  shrinkWrap: ${node.shrinkWrap},
  physics: ${node.physics},
  children: [$children],
)""";
    }

    if (node is TextButtonNode) {
      return "TextButton(onPressed: ${node.action}, child: Text('${node.text}', style: ${node.style != null ? _renderTextStyle(node.style!) : 'null'}))";
    }

    if (node is TextFieldNode) {
      return "UIFactory.textField(label: '${node.label}', obscureText: ${node.obscureText})";
    }

    if (node is SingleChildScrollViewNode) {
      return "SingleChildScrollView(padding: ${node.padding != null ? _renderEdgeInsets(node.padding!) : 'EdgeInsets.zero'}, child: ${render(node.child)})";
    }

    if (node is SectionNode) {
      return render(node.child);
    }

    if (node is AppBarNode) {
      final actions = node.actions.map((a) => render(a)).join(", ");
      final props = [
        "title: Text('${node.title}')",
        "centerTitle: ${node.centerTitle}",
        if (node.actions.isNotEmpty) "actions: [$actions]",
        if (node.backgroundColor != null) "backgroundColor: ${_renderColor(node.backgroundColor!)}",
      ].join(", ");
      return "AppBar($props)";
    }

    return "/* Unknown Node */ const SizedBox()";
  }

  static String _renderEdgeInsets(EdgeInsetsExpr expr) {
    switch (expr.kind) {
      case 'all': return "const EdgeInsets.all(${expr.value})";
      case 'symmetric': 
        return "const EdgeInsets.symmetric(horizontal: ${expr.horizontal ?? 0}, vertical: ${expr.vertical ?? 0})";
      case 'only':
        return "const EdgeInsets.only(top: ${expr.top ?? 0}, bottom: ${expr.bottom ?? 0}, left: ${expr.left ?? 0}, right: ${expr.right ?? 0})";
      case 'token':
        return "AppDesign.padding${expr.token![0].toUpperCase()}${expr.token!.substring(1)}";
      default: return "EdgeInsets.zero";
    }
  }

  static String _renderAlignment(AlignmentExpr expr) {
    switch (expr.value) {
      case 'center': return 'Alignment.center';
      case 'topLeft': return 'Alignment.topLeft';
      case 'topRight': return 'Alignment.topRight';
      case 'bottomLeft': return 'Alignment.bottomLeft';
      case 'bottomRight': return 'Alignment.bottomRight';
      case 'centerLeft': return 'Alignment.centerLeft';
      case 'centerRight': return 'Alignment.centerRight';
      case 'topCenter': return 'Alignment.topCenter';
      case 'bottomCenter': return 'Alignment.bottomCenter';
      default: return 'Alignment.center';
    }
  }

  static String _renderColor(ColorExpr expr) {
    switch (expr.kind) {
      case 'material': return "Colors.${expr.value}";
      case 'hex': return "Color(0x${expr.value})";
      case 'design': return "AppDesign.color${expr.value[0].toUpperCase()}${expr.value.substring(1)}";
      default: return "Colors.transparent";
    }
  }

  static String _renderTextStyle(TextStyleExpr expr) {
    if (expr.themeStyle != null) {
      String code = "Theme.of(context).textTheme.${expr.themeStyle}";
      if (expr.fontSize != null || expr.fontWeight != null || expr.color != null || expr.letterSpacing != null) {
        final props = [
          if (expr.fontSize != null) "fontSize: ${expr.fontSize}",
          if (expr.fontWeight != null) "fontWeight: FontWeight.${expr.fontWeight}",
          if (expr.color != null) "color: ${_renderColor(expr.color!)}",
          if (expr.letterSpacing != null) "letterSpacing: ${expr.letterSpacing}",
        ].join(", ");
        code += "?.copyWith($props)";
      }
      return code;
    }
    
    final props = [
      if (expr.fontSize != null) "fontSize: ${expr.fontSize}",
      if (expr.fontWeight != null) "fontWeight: FontWeight.${expr.fontWeight}",
      if (expr.color != null) "color: ${_renderColor(expr.color!)}",
      if (expr.letterSpacing != null) "letterSpacing: ${expr.letterSpacing}",
    ].join(", ");
    return "TextStyle($props)";
  }
}
