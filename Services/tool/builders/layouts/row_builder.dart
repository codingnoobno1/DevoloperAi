import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';

class RowLayoutBuilder implements LayoutBuilder {
  @override
  WidgetNode build(LayoutModel config, List<WidgetNode> children, AppModel model) {
    return RowNode(
      children: children,
      spacing: 8.0, // AppDesign.spacingSm
    );
  }
}
