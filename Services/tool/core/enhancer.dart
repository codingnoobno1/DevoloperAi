import 'ast.dart';
import 'expressions.dart';

/// Enhances the AST tree with layout intelligence.
/// This pass can automatically inject padding, spacing, and safety wrappers.
class ASTEnhancer {
  static WidgetNode enhance(WidgetNode node) {
    // 1. Recursive enhancement of children
    WidgetNode enhancedNode = _enhanceNode(node);

    // 2. Structural optimizations
    if (enhancedNode is ColumnNode && enhancedNode.spacing != null) {
      // Auto-spacing logic is handled by the renderer for now, 
      // but could be explicitly transformed here into SizedBoxNodes.
    }

    return enhancedNode;
  }

  static WidgetNode _enhanceNode(WidgetNode node) {
    if (node is ColumnNode) {
      return ColumnNode(
        children: node.children.map(enhance).toList(),
        crossAxisAlignment: node.crossAxisAlignment,
        mainAxisAlignment: node.mainAxisAlignment,
        mainAxisSize: node.mainAxisSize,
        spacing: node.spacing,
      );
    }

    if (node is RowNode) {
      return RowNode(
        children: node.children.map(enhance).toList(),
        crossAxisAlignment: node.crossAxisAlignment,
        mainAxisAlignment: node.mainAxisAlignment,
        spacing: node.spacing,
      );
    }

    if (node is PaddingNode) {
      return PaddingNode(child: enhance(node.child), padding: node.padding);
    }

    if (node is CenterNode) {
      return CenterNode(child: enhance(node.child));
    }

    if (node is ExpandedNode) {
      return ExpandedNode(child: enhance(node.child), flex: node.flex);
    }

    if (node is ScaffoldNode) {
      // Auto-wrap scaffold body in SafeArea if it's not there
      WidgetNode? body = node.body != null ? enhance(node.body!) : null;
      if (body != null && body is! SafeAreaNode) {
        body = SafeAreaNode(child: body);
      }

      return ScaffoldNode(
        body: body,
        appBar: node.appBar != null ? enhance(node.appBar!) : null,
        bottomNavigationBar: node.bottomNavigationBar != null ? enhance(node.bottomNavigationBar!) : null,
        floatingActionButton: node.floatingActionButton != null ? enhance(node.floatingActionButton!) : null,
        backgroundColor: node.backgroundColor,
      );
    }

    if (node is SafeAreaNode) {
      return SafeAreaNode(child: enhance(node.child));
    }

    if (node is SizedBoxNode) {
      return SizedBoxNode(child: node.child != null ? enhance(node.child!) : null, width: node.width, height: node.height);
    }

    if (node is ContainerNode) {
      return ContainerNode(
        child: node.child != null ? enhance(node.child!) : null,
        padding: node.padding,
        margin: node.margin,
        decoration: node.decoration,
        width: node.width,
        height: node.height,
        alignment: node.alignment,
      );
    }

    if (node is CardNode) {
      return CardNode(child: enhance(node.child), elevation: node.elevation, color: node.color, shape: node.shape);
    }

    if (node is SectionNode) {
      // Sections can trigger specific enhancements
      WidgetNode child = enhance(node.child);
      if (node.name == 'header') {
        // Headers might get auto-padding or specific styling
      }
      return SectionNode(name: node.name, child: child, sticky: node.sticky);
    }

    if (node is ListViewNode) {
      return ListViewNode(
        children: node.children.map(enhance).toList(),
        padding: node.padding,
        shrinkWrap: node.shrinkWrap,
        physics: node.physics,
      );
    }

    if (node is GridViewNode) {
      return GridViewNode(
        children: node.children.map(enhance).toList(),
        crossAxisCount: node.crossAxisCount,
        mainAxisSpacing: node.mainAxisSpacing,
        crossAxisSpacing: node.crossAxisSpacing,
        childAspectRatio: node.childAspectRatio,
        shrinkWrap: node.shrinkWrap,
        physics: node.physics,
      );
    }

    if (node is SingleChildScrollViewNode) {
      return SingleChildScrollViewNode(child: enhance(node.child), padding: node.padding);
    }

    return node;
  }
}
