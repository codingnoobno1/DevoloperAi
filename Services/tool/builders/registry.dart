import '../core/ast.dart';
import '../core/renderer.dart';
import '../core/interfaces.dart';
import '../core/models.dart';
import '../expansion/ast_interpreter.dart';
import 'animations/fade_builder.dart';
import 'animations/slide_builder.dart';
import 'animations/scale_builder.dart';

// Widget Builders
import 'widgets/text_builder.dart';
import 'widgets/button_builder.dart';
import 'widgets/card_builder.dart';
import 'widgets/input_builder.dart';
import 'widgets/image_builder.dart';
import 'widgets/list_tile_builder.dart';
import 'widgets/divider_builder.dart';
import 'widgets/container_builder.dart';
import 'widgets/padding_builder.dart';
import 'widgets/map_builder.dart';

// Layout Builders
import 'layouts/auth_builder.dart';
import 'layouts/dashboard_builder.dart';
import 'layouts/column_builder.dart';
import 'layouts/row_builder.dart';
import 'layouts/tab_view_builder.dart';
import 'layouts/list_view_builder.dart';
import 'layouts/map_view_builder.dart';
import 'layouts/home_builder.dart';

// Template Builders
import 'templates/auth_login.dart';
import 'templates/dashboard_stats.dart';
import 'templates/promo_card.dart';

class BuilderRegistry {
  static final Map<String, UnifiedBuilder> components = {
    // Widgets
    'Text': UnifiedBuilder.widget(TextWidgetBuilder()),
    'Button': UnifiedBuilder.widget(ButtonWidgetBuilder()),
    'Card': UnifiedBuilder.widget(CardWidgetBuilder()),
    'card': UnifiedBuilder.widget(CardWidgetBuilder()),
    'TextField': UnifiedBuilder.widget(InputWidgetBuilder()),
    'Image': UnifiedBuilder.widget(ImageWidgetBuilder()),
    'ListTile': UnifiedBuilder.widget(ListTileWidgetBuilder()),
    'Divider': UnifiedBuilder.widget(DividerWidgetBuilder()),
    'Container': UnifiedBuilder.widget(ContainerWidgetBuilder()),
    'container': UnifiedBuilder.widget(ContainerWidgetBuilder()),
    'Padding': UnifiedBuilder.widget(PaddingWidgetBuilder()),
    'MapView': UnifiedBuilder.widget(MapWidgetBuilder()),
    'map': UnifiedBuilder.widget(MapWidgetBuilder()),
    
    // Layouts
    'Column': UnifiedBuilder.layout(ColumnLayoutBuilder()),
    'vertical': UnifiedBuilder.layout(ColumnLayoutBuilder()),
    'Row': UnifiedBuilder.layout(RowLayoutBuilder()),
    'horizontal': UnifiedBuilder.layout(RowLayoutBuilder()),
    'TabView': UnifiedBuilder.layout(TabViewLayoutBuilder()),
    'tabs': UnifiedBuilder.layout(TabViewLayoutBuilder()),
    'ListView': UnifiedBuilder.layout(ListViewLayoutBuilder()),
    'list': UnifiedBuilder.layout(ListViewLayoutBuilder()),
    'MapViewLayout': UnifiedBuilder.layout(MapViewLayoutBuilder()), // Rename to avoid conflict
  };

  static final Map<String, dynamic> templates = {
    'auth_login': AuthLoginTemplate(),
    'dashboard_stats': DashboardStatsTemplate(),
    'promo_card': PromoCardTemplate(),
    'header_body': ColumnLayoutBuilder(),
    'default_stack': ColumnLayoutBuilder(),
  };

  /// Build a widget — unified path for all components.
  static WidgetNode buildWidget(WidgetModel config, AppModel model) {
    // AST-first path (from IR)
    if (config.properties.containsKey('ast') && config.properties['ast'] != null) {
      return _buildFromAST(config);
    } 
    if (config.ast != null) {
      return ASTInterpreter.interpret(Map<String, dynamic>.from(config.ast!), config.properties);
    }

    // Template path
    if (config.template != null && templates.containsKey(config.template)) {
      final template = templates[config.template]!;
      if (template is WidgetBuilder) {
        return (template as WidgetBuilder).build(config, model);
      }
      // If it's a raw node or generic template
      if (template is WidgetNode) return template;
    }

    // Unified Registry Path
    final builder = getBuilder(config.type);
    WidgetNode node = builder.build(config: config, model: model);

    // Apply Animations
    if (config.animation != null) {
      final type = config.animation!['type'];
      String code = DartRenderer.render(node);
      switch (type) {
        case 'fade': code = FadeAnimationBuilder.wrap(config, code); break;
        case 'slide': code = SlideAnimationBuilder.wrap(config, code); break;
        case 'scale': code = ScaleAnimationBuilder.wrap(config, code); break;
      }
      node = RawNode(code);
    }

    return node;
  }

  /// Build a layout.
  static WidgetNode buildLayout(LayoutModel config, List<WidgetNode> children, AppModel model) {
    // Template path
    if (config.template != null && templates.containsKey(config.template)) {
      final template = templates[config.template]!;
      if (template is LayoutBuilder) {
        return (template as LayoutBuilder).build(config, children, model);
      }
      return template.build(config.props, children, model);
    }

    // AST path
    if (config.type == 'ASTLayout' && config.ast != null) {
      return ASTInterpreter.interpret(Map<String, dynamic>.from(config.ast!), config.props, injectedChildren: children);
    }

    // Unified Registry Path
    final builder = getBuilder(config.type);
    return builder.build(config: config, model: model, children: children);
  }

  static UnifiedBuilder getBuilder(String type) {
    final builder = components[type];
    if (builder == null) {
      throw Exception("❌ Unknown component type: '$type'. Check registry.dart.");
    }
    return builder;
  }

  static WidgetNode _buildFromAST(WidgetModel config) {
    final astDef = config.properties['ast'];
    if (astDef is Map) {
      return ASTInterpreter.interpret(Map<String, dynamic>.from(astDef), config.properties);
    }
    return const RawNode('const SizedBox() /* AST Error */');
  }
}

/// A wrapper to unify WidgetBuilder and LayoutBuilder calls.
class UnifiedBuilder {
  final WidgetBuilder? widgetBuilder;
  final LayoutBuilder? layoutBuilder;

  UnifiedBuilder.widget(this.widgetBuilder) : layoutBuilder = null;
  UnifiedBuilder.layout(this.layoutBuilder) : widgetBuilder = null;

  WidgetNode build({required dynamic config, required AppModel model, List<WidgetNode>? children}) {
    if (widgetBuilder != null) {
      final wConfig = config is WidgetModel ? config : WidgetModel.fromJson({
        "id": config.id,
        "type": config.type,
        "properties": config.props,
      });
      return widgetBuilder!.build(wConfig, model);
    }
    
    if (layoutBuilder != null) {
      final lConfig = config is LayoutModel ? config : LayoutModel(
        type: config.type,
        children: [],
        slots: [],
        props: config.properties,
      );
      return layoutBuilder!.build(lConfig, children ?? [], model);
    }

    throw Exception("UnifiedBuilder: No underlying builder found.");
  }
}
