import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';

class DividerWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) => RawNode("const Divider()");
}
