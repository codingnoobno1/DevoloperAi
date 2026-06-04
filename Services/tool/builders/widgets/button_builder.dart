import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/binding_resolver.dart';

class ButtonWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    final text = config.properties['text'] ?? 'Continue';
    final actionCode = config.action != null 
        ? "() => ${BindingResolver.resolveAction(config.action!)}"
        : "() {}";

    return ButtonNode(
      text: text,
      action: actionCode,
      fullWidth: true,
    );
  }
}
