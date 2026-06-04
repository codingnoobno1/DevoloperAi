import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';

class MapWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    return ContainerNode(
      height: config.properties['height']?.toDouble() ?? 300.0,
      decoration: "BoxDecoration(color: Colors.grey[300], borderRadius: BorderRadius.circular(12))",
      child: CenterNode(
        child: ColumnNode(
          mainAxisAlignment: "MainAxisAlignment.center",
          children: [
            const IconNode("Icons.map", size: 48, color: ColorExpr.material("grey")),
            const SizedBoxNode(height: 8),
            TextNode("Map View Placeholder", style: TextStyleExpr(color: ColorExpr.material("grey"), fontWeight: "bold")),
          ],
        ),
      ),
    );
  }
}
