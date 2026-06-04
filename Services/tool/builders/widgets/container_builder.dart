import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../registry.dart';

class ContainerWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    final children = config.children.map((c) => BuilderRegistry.buildWidget(c, model)).toList();
    
    WidgetNode? child;
    if (children.isEmpty) {
      child = null;
    } else if (children.length == 1) {
      child = children.first;
    } else {
      child = ColumnNode(children: children);
    }

    return ContainerNode(
      child: child,
      width: config.properties['width']?.toDouble(),
      height: config.properties['height']?.toDouble(),
    );
  }
}
