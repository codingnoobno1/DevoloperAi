import 'expressions.dart';

/// Abstract Syntax Tree (AST) for Flutter Widgets.
/// Nodes are pure data structures with no rendering logic.
abstract class WidgetNode {
  const WidgetNode();
}

class RawNode extends WidgetNode {
  final String code;
  const RawNode(this.code);
}

class ColumnNode extends WidgetNode {
  final List<WidgetNode> children;
  final String crossAxisAlignment;
  final String mainAxisAlignment;
  final String mainAxisSize;
  final double? spacing;

  const ColumnNode({
    required this.children,
    this.crossAxisAlignment = 'CrossAxisAlignment.start',
    this.mainAxisAlignment = 'MainAxisAlignment.start',
    this.mainAxisSize = 'MainAxisSize.min',
    this.spacing,
  });
}

class RowNode extends WidgetNode {
  final List<WidgetNode> children;
  final String crossAxisAlignment;
  final String mainAxisAlignment;
  final double? spacing;

  const RowNode({
    required this.children,
    this.crossAxisAlignment = 'CrossAxisAlignment.center',
    this.mainAxisAlignment = 'MainAxisAlignment.start',
    this.spacing,
  });
}

class PaddingNode extends WidgetNode {
  final WidgetNode child;
  final EdgeInsetsExpr padding;

  const PaddingNode({required this.child, required this.padding});
}

class CenterNode extends WidgetNode {
  final WidgetNode child;
  const CenterNode({required this.child});
}

class ExpandedNode extends WidgetNode {
  final WidgetNode child;
  final int flex;
  const ExpandedNode({required this.child, this.flex = 1});
}

class ScaffoldNode extends WidgetNode {
  final WidgetNode? body;
  final WidgetNode? appBar;
  final WidgetNode? bottomNavigationBar;
  final WidgetNode? floatingActionButton;
  final ColorExpr? backgroundColor;

  const ScaffoldNode({
    this.body,
    this.appBar,
    this.bottomNavigationBar,
    this.floatingActionButton,
    this.backgroundColor,
  });
}

class SafeAreaNode extends WidgetNode {
  final WidgetNode child;
  const SafeAreaNode({required this.child});
}

class SizedBoxNode extends WidgetNode {
  final WidgetNode? child;
  final double? width;
  final double? height;

  const SizedBoxNode({this.child, this.width, this.height});
}

class ContainerNode extends WidgetNode {
  final WidgetNode? child;
  final EdgeInsetsExpr? padding;
  final EdgeInsetsExpr? margin;
  final String? decoration; // Still string for now, but better as DecorationExpr later
  final double? width;
  final double? height;
  final AlignmentExpr? alignment;

  const ContainerNode({
    this.child,
    this.padding,
    this.margin,
    this.decoration,
    this.width,
    this.height,
    this.alignment,
  });
}

class CardNode extends WidgetNode {
  final WidgetNode child;
  final double? elevation;
  final ColorExpr? color;
  final String? shape;

  const CardNode({
    required this.child,
    this.elevation,
    this.color,
    this.shape,
  });
}

class ButtonNode extends WidgetNode {
  final String text;
  final String action;
  final String variant; // primary, secondary, outline
  final String size;    // sm, md, lg
  final bool fullWidth;

  const ButtonNode({
    required this.text,
    required this.action,
    this.variant = 'primary',
    this.size = 'md',
    this.fullWidth = true,
  });
}

class TextNode extends WidgetNode {
  final String text;
  final TextStyleExpr? style;
  const TextNode(this.text, {this.style});
}

class IconNode extends WidgetNode {
  final String icon;
  final double? size;
  final ColorExpr? color;
  const IconNode(this.icon, {this.size, this.color});
}

class SectionNode extends WidgetNode {
  final String name; // e.g. "header", "body", "footer"
  final WidgetNode child;
  final bool sticky;
  const SectionNode({required this.name, required this.child, this.sticky = false});
}

class TextButtonNode extends WidgetNode {
  final String text;
  final String action;
  final TextStyleExpr? style;

  const TextButtonNode({required this.text, required this.action, this.style});
}

class SingleChildScrollViewNode extends WidgetNode {
  final WidgetNode child;
  final EdgeInsetsExpr? padding;

  const SingleChildScrollViewNode({required this.child, this.padding});
}

class TextFieldNode extends WidgetNode {
  final String label;
  final bool obscureText;
  final String? controller;

  const TextFieldNode({required this.label, this.obscureText = false, this.controller});
}

class ListViewNode extends WidgetNode {
  final List<WidgetNode> children;
  final EdgeInsetsExpr? padding;
  final bool shrinkWrap;
  final String physics;

  const ListViewNode({
    required this.children,
    this.padding,
    this.shrinkWrap = false,
    this.physics = 'AlwaysScrollableScrollPhysics()',
  });
}

class GridViewNode extends WidgetNode {
  final List<WidgetNode> children;
  final int crossAxisCount;
  final double mainAxisSpacing;
  final double crossAxisSpacing;
  final double childAspectRatio;
  final bool shrinkWrap;
  final String physics;

  const GridViewNode({
    required this.children,
    required this.crossAxisCount,
    this.mainAxisSpacing = 0.0,
    this.crossAxisSpacing = 0.0,
    this.childAspectRatio = 1.0,
    this.shrinkWrap = false,
    this.physics = 'AlwaysScrollableScrollPhysics()',
  });
}
class AppBarNode extends WidgetNode {
  final String title;
  final bool centerTitle;
  final List<WidgetNode> actions;
  final ColorExpr? backgroundColor;

  const AppBarNode({
    required this.title,
    this.centerTitle = true,
    this.actions = const [],
    this.backgroundColor,
  });
}
