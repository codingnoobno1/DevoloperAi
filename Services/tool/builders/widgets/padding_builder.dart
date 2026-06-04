import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';
import '../registry.dart';

class PaddingWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    final children = config.children.map((c) => BuilderRegistry.buildWidget(c, model)).toList();
    
    if (children.isEmpty) {
      // If no children, padding is useless, but we must return a node.
      // Returning a zero-size SizedBox or similar is safer than an empty column.
      return const SizedBoxNode(width: 0, height: 0);
    }

    WidgetNode child = children.length == 1 
        ? children.first 
        : ColumnNode(children: children);

    return PaddingNode(
      padding: const EdgeInsetsExpr.all(16),
      child: child,
    );
  }
}
