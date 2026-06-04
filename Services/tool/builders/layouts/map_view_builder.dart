import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';

class MapViewLayoutBuilder implements LayoutBuilder {
  @override
  WidgetNode build(LayoutModel config, List<WidgetNode> children, AppModel model) {
    return RawNode("""
Container(
  height: 300,
  decoration: BoxDecoration(
    color: Colors.grey.shade200,
    borderRadius: AppDesign.borderRadiusLg,
  ),
  child: const Center(
    child: Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Icon(Icons.map_rounded, size: 48, color: Colors.grey),
        SizedBox(height: 8),
        Text("Map View Placeholder", style: TextStyle(color: Colors.grey)),
      ],
    ),
  ),
)""");
  }
}
