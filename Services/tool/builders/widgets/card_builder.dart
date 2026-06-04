import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';
import '../registry.dart';

class CardWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    final title = config.properties['title'] ?? '';
    final subtitle = config.properties['subtitle'] ?? '';
    final align = config.properties['alignment'] ?? 'start';
    final crossAlign = align == 'center' ? 'CrossAxisAlignment.center' : 'CrossAxisAlignment.start';

    final childrenNodes = config.children.map((c) => BuilderRegistry.buildWidget(c, model)).toList();

    final cardContent = ColumnNode(
      crossAxisAlignment: crossAlign,
      children: [
        if (title.isNotEmpty)
          TextNode(title, style: const TextStyleExpr(themeStyle: "titleLarge", fontWeight: "bold")),
        if (subtitle.isNotEmpty)
          PaddingNode(
            padding: const EdgeInsetsExpr.only(top: 4),
            child: TextNode(subtitle, style: const TextStyleExpr(themeStyle: "bodyMedium")),
          ),
        if (childrenNodes.isNotEmpty)
          PaddingNode(
            padding: const EdgeInsetsExpr.only(top: 16),
            child: ColumnNode(
              crossAxisAlignment: crossAlign,
              spacing: 8.0, 
              children: childrenNodes,
            ),
          ),
      ],
    );

    return CardNode(
      child: PaddingNode(
        padding: const EdgeInsetsExpr.token("card"),
        child: cardContent,
      ),
      elevation: 4.0,
    );
  }
}
