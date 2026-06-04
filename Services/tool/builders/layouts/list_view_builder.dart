import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';

class ListViewLayoutBuilder implements LayoutBuilder {
  @override
  WidgetNode build(LayoutModel config, List<WidgetNode> children, AppModel model) {
    return ListViewNode(
      padding: const EdgeInsetsExpr.token("page"),
      children: children,
    );
  }
}
