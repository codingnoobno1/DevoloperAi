import '../../core/models.dart';
import '../../core/binding_resolver.dart';

class ScaleAnimationBuilder {
  static String wrap(WidgetModel config, String widgetCode) {
    final animation = config.animation;
    final condition = animation?['when'] != null ? BindingResolver.resolve(animation!['when']) : "true";
    
    return """
AnimatedScale(
  scale: ($condition == true) ? 1.0 : 0.0,
  duration: const Duration(milliseconds: 400),
  child: $widgetCode,
)
""";
  }
}
