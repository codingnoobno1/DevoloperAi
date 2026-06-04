import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';
import '../../utils/string_utils.dart';

class PromoCardTemplate implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    final title = config.properties['title'] ?? StringUtils.capitalize(config.id);
    final subtitle = config.properties['subtitle'] ?? 'Exclusive Offer';
    
    return RawNode("""
UIFactory.card(
  child: Padding(
    padding: AppDesign.paddingCard,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('$title', style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.bold)),
        const SizedBox(height: 4),
        Text('$subtitle', style: Theme.of(context).textTheme.bodyMedium),
        const SizedBox(height: 16),
        ${config.action != null ? "ElevatedButton(onPressed: () => NavigationService.navigateTo('${config.action!['target']}'), child: const Text('View Details'))" : "const SizedBox()"}
      ],
    ),
  ),
  elevation: 4.0,
)
""");
  }
}
