import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';

class DashboardLayoutBuilder implements LayoutBuilder {
  @override
  WidgetNode build(LayoutModel config, List<WidgetNode> children, AppModel model) {
    return SingleChildScrollViewNode(
      padding: const EdgeInsetsExpr.token("page"),
      child: ColumnNode(
        crossAxisAlignment: "CrossAxisAlignment.start",
        children: [
          const TextNode("Overview", style: TextStyleExpr(fontSize: 28, fontWeight: "bold", letterSpacing: -0.5)),
          const SizedBoxNode(height: 8),
          const TextNode("Welcome back! Here's your status for today.", style: TextStyleExpr(color: ColorExpr.material("grey"), fontSize: 16)),
          const SizedBoxNode(height: 24),
          GridViewNode(
            crossAxisCount: 2,
            mainAxisSpacing: 16.0, 
            crossAxisSpacing: 16.0, 
            childAspectRatio: 0.9,
            shrinkWrap: true,
            physics: "const NeverScrollableScrollPhysics()",
            children: children,
          ),
        ],
      ),
    );
  }
}
