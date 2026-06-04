import '../../core/models.dart';
import '../../core/binding_resolver.dart';

class FadeAnimationBuilder {
  static String wrap(WidgetModel config, String widgetCode) {
    final animation = config.animation;
    if (animation == null) return widgetCode;

    final condition = animation['when'] != null 
        ? BindingResolver.resolve(animation['when'] as String) 
        : "true";
    
    final duration = animation['duration'] ?? 300;

    return """
AnimatedOpacity(
  opacity: ($condition == true) ? 1.0 : 0.0,
  duration: const Duration(milliseconds: $duration),
  child: $widgetCode,
)
""";
  }
}
