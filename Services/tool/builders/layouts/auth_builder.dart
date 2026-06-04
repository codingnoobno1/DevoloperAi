import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';

class AuthLayoutBuilder implements LayoutBuilder {
  @override
  WidgetNode build(LayoutModel config, List<WidgetNode> children, AppModel model) {
    final variant = config.variant ?? 'centered';

    switch (variant) {
      case 'split':
        return RowNode(
          children: [
            ExpandedNode(
              child: ContainerNode(
                decoration: "BoxDecoration(color: Theme.of(context).primaryColor)",
                child: const CenterNode(
                  child: ColumnNode(
                    mainAxisAlignment: "MainAxisAlignment.center",
                    children: [
                      IconNode("Icons.bolt", size: 100, color: ColorExpr.material("white")),
                      SizedBoxNode(height: 16),
                      TextNode("THUNDER", style: TextStyleExpr(color: ColorExpr.material("white"), fontSize: 32, fontWeight: "black", letterSpacing: 4)),
                    ],
                  ),
                ),
              ),
            ),
            ExpandedNode(
              child: PaddingNode(
                padding: const EdgeInsetsExpr.all(32),
                child: ColumnNode(
                  mainAxisAlignment: "MainAxisAlignment.center",
                  crossAxisAlignment: "CrossAxisAlignment.start",
                  children: [
                    if (children.isNotEmpty)
                      ColumnNode(
                        spacing: 16.0,
                        children: children,
                      ),
                  ],
                ),
              ),
            ),
          ],
        );
      case 'minimal':
        return PaddingNode(
          padding: const EdgeInsetsExpr.symmetric(horizontal: 24),
          child: ColumnNode(
            crossAxisAlignment: "CrossAxisAlignment.start",
            children: [
              const SizedBoxNode(height: 80),
              const TextNode("Sign In", style: TextStyleExpr(fontSize: 36, fontWeight: "bold")),
              const SizedBoxNode(height: 8),
              const TextNode("Welcome back, you've been missed!", style: TextStyleExpr(fontSize: 16, color: ColorExpr.material("grey"))),
              if (children.isNotEmpty)
                ColumnNode(
                  spacing: 16.0,
                  children: children,
                ),
            ],
          ),
        );
      case 'centered':
      default:
        return CenterNode(
          child: SingleChildScrollViewNode(
            padding: const EdgeInsetsExpr.token("lg"),
            child: CardNode(
              child: PaddingNode(
                padding: const EdgeInsetsExpr.token("lg"),
                child: ColumnNode(
                  mainAxisSize: "MainAxisSize.min",
                  children: [
                    const IconNode("Icons.lock_outline", size: 48, color: ColorExpr.material("teal")),
                    const SizedBoxNode(height: 16),
                    const TextNode("Secure Access", style: TextStyleExpr(fontSize: 24, fontWeight: "bold")),
                    const SizedBoxNode(height: 32),
                    if (children.isNotEmpty)
                      ColumnNode(
                        spacing: 16.0,
                        children: children,
                      ),
                  ],
                ),
              ),
            ),
          ),
        );
    }
  }
}
