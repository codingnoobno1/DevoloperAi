import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class CardsVariant1Builder implements WidgetBuilder {
  @override
  String build(WidgetModel config, AppModel model) {
    return 'Container(child: const Text(variant))';
  }
}
