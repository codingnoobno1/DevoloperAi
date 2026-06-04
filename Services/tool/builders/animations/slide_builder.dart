import '../../core/models.dart';
import '../../core/binding_resolver.dart';

class SlideAnimationBuilder {
  static String wrap(WidgetModel config, String widgetCode) {
    final animation = config.animation;
    final condition = animation?['when'] != null ? BindingResolver.resolve(animation!['when']) : "true";
    
    return """
AnimatedSlide(
  offset: ($condition == true) ? Offset.zero : const Offset(0, 1),
  duration: const Duration(milliseconds: 500),
  child: $widgetCode,
)
""";
  }
}
