import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';

class DashboardStatsTemplate implements TemplateBuilder {
  @override
  WidgetNode build(Map<String, dynamic> props, List<WidgetNode> children, AppModel model) {
    final title = props['title'] ?? 'Daily Performance';

    return ColumnNode(
      crossAxisAlignment: "CrossAxisAlignment.start",
      children: [
        // Header Section
        SectionNode(
          name: "header",
          child: RowNode(
            mainAxisAlignment: "MainAxisAlignment.spaceBetween",
            children: [
              ColumnNode(
                children: [
                  TextNode(title, style: const TextStyleExpr(fontSize: 24, fontWeight: "bold")),
                  const TextNode("Last updated: Just now", style: TextStyleExpr(fontSize: 13, color: ColorExpr.material("grey"))),
                ],
              ),
              const CardNode(
                child: PaddingNode(
                  padding: EdgeInsetsExpr.token("sm"),
                  child: IconNode("Icons.notifications_none_rounded", color: ColorExpr.material("teal")),
                ),
              ),
            ],
          ),
        ),

        const SizedBoxNode(height: 24),

        // Summary Section
        SectionNode(
          name: "summary",
          child: RowNode(
            children: [
              ExpandedNode(child: _miniStat("Earnings", "\\\$1,240", "Icons.trending_up", "green")),
              const SizedBoxNode(width: 16),
              ExpandedNode(child: _miniStat("Rides", "42", "Icons.directions_car", "blue")),
            ],
          ),
        ),

        const SizedBoxNode(height: 24),

        const TextNode("Recent Activity", style: TextStyleExpr(fontSize: 18, fontWeight: "bold")),
        const SizedBoxNode(height: 16),

        // Grid Section
        SectionNode(
          name: "grid",
          child: GridViewNode(
            crossAxisCount: 2,
            mainAxisSpacing: 16.0, 
            crossAxisSpacing: 16.0, 
            childAspectRatio: 1.1,
            shrinkWrap: true,
            physics: "const NeverScrollableScrollPhysics()",
            children: children,
          ),
        ),
      ],
    );
  }

  WidgetNode _miniStat(String label, String value, String icon, String color) {
    return CardNode(
      child: PaddingNode(
        padding: const EdgeInsetsExpr.token("md"),
        child: ColumnNode(
          crossAxisAlignment: "CrossAxisAlignment.start",
          children: [
            IconNode(icon, color: ColorExpr.material(color), size: 20),
            const SizedBoxNode(height: 8), 
            TextNode(label, style: const TextStyleExpr(color: ColorExpr.material("grey"), fontSize: 12)),
            TextNode(value, style: const TextStyleExpr(fontSize: 20, fontWeight: "bold")),
          ],
        ),
      ),
    );
  }
}
