import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';

class ColumnLayoutBuilder implements LayoutBuilder {
  @override
  WidgetNode build(LayoutModel config, List<WidgetNode> children, AppModel model) {
    final align = config.variant == 'center' ? 'CrossAxisAlignment.center' : 'CrossAxisAlignment.start';
    
    return PaddingNode(
      padding: const EdgeInsetsExpr.token('page'),
      child: ColumnNode(
        crossAxisAlignment: align,
        spacing: 16.0, 
        children: children,
      ),
    );
  }
}
