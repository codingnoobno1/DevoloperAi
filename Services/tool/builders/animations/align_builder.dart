import '../../core/models.dart';
import '../../core/binding_resolver.dart';

class AlignAnimationBuilder {
  static String wrap(WidgetModel config, String widgetCode) {
    final animation = config.animation;
    final condition = animation?['when'] != null ? BindingResolver.resolve(animation!['when']) : 'true';
    return 'AnimatedContainer(duration: const Duration(milliseconds: 300), child: ' + widgetCode + ')';
  }
}
