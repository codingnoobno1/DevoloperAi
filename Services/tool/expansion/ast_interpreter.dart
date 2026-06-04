import '../core/ast.dart';
import '../core/expressions.dart';

/// Interprets a JSON AST definition into a live WidgetNode tree.
/// This is the brain of the rule engine — it converts data → structure.
class ASTInterpreter {
  /// Resolve {{bind:field}} placeholders against a properties map.
  static String _bind(String text, Map<String, dynamic> props) {
    return text.replaceAllMapped(RegExp(r'\{\{bind:(\w+)\}\}'), (m) {
      final key = m.group(1)!;
      return props[key]?.toString() ?? key;
    });
  }

  /// Main entry: convert a JSON AST map into a WidgetNode.
  /// [astJson]  — the AST definition from expansion_rules.json
  /// [props]    — runtime properties from the user's widget config
  /// [injectedChildren] — children to inject at "slot" positions
  static WidgetNode interpret(
    Map<String, dynamic> astJson,
    Map<String, dynamic> props, {
    List<WidgetNode>? injectedChildren,
  }) {
    final type = astJson['type'] as String;

    switch (type) {
      case 'ColumnNode':
        return _columnNode(astJson, props, injectedChildren);
      case 'RowNode':
        return _rowNode(astJson, props, injectedChildren);
      case 'PaddingNode':
        return _paddingNode(astJson, props, injectedChildren);
      case 'CenterNode':
        return CenterNode(child: _child(astJson, props, injectedChildren));
      case 'ContainerNode':
        return _containerNode(astJson, props, injectedChildren);
      case 'CardNode':
        return CardNode(child: _child(astJson, props, injectedChildren));
      case 'ScaffoldNode':
        return _scaffoldNode(astJson, props, injectedChildren);
      case 'SafeAreaNode':
        return SafeAreaNode(child: _child(astJson, props, injectedChildren));
      case 'SizedBoxNode':
        return _sizedBoxNode(astJson);
      case 'TextNode':
        return _textNode(astJson, props);
      case 'IconNode':
        return _iconNode(astJson);
      case 'ButtonNode':
        return _buttonNode(astJson, props);
      case 'TextFieldNode':
        return _textFieldNode(astJson, props);
      case 'SingleChildScrollViewNode':
        return _scrollNode(astJson, props, injectedChildren);
      case 'ListViewNode':
        return _listViewNode(astJson, props, injectedChildren);
      case 'AppBarNode':
        return _appBarNode(astJson, props);
      case 'ExpandedNode':
        return ExpandedNode(child: _child(astJson, props, injectedChildren));
      case 'SectionNode':
        final p = astJson['props'] ?? {};
        return SectionNode(
          name: _bind(p['name'] ?? 'section', props),
          child: _child(astJson, props, injectedChildren),
        );
      case 'DividerNode':
        return const RawNode('const Divider()');
      case 'ListTileNode':
        return _listTileNode(astJson, props);
      default:
        return const RawNode('const SizedBox() /* unrecognized AST node */');
    }
  }

  // ─── Child resolution ────────────────────────────────────

  /// Resolve a single "child" key.
  static WidgetNode _child(
    Map<String, dynamic> astJson,
    Map<String, dynamic> props,
    List<WidgetNode>? injectedChildren,
  ) {
    if (astJson['child'] == null) {
      return const RawNode('const SizedBox()');
    }
    return interpret(
      Map<String, dynamic>.from(astJson['child']),
      props,
      injectedChildren: injectedChildren,
    );
  }

  /// Resolve a "children" list, handling slots and repeats.
  static List<WidgetNode> _children(
    Map<String, dynamic> astJson,
    Map<String, dynamic> props,
    List<WidgetNode>? injectedChildren,
  ) {
    final List<WidgetNode> result = [];

    // Static children defined in the AST rule
    if (astJson['children'] != null) {
      for (var childDef in (astJson['children'] as List)) {
        final childMap = Map<String, dynamic>.from(childDef);
        final repeat = childMap['repeat'] as int?;
        if (repeat != null && repeat > 1) {
          for (int i = 0; i < repeat; i++) {
            result.add(interpret(childMap, props, injectedChildren: injectedChildren));
          }
        } else {
          result.add(interpret(childMap, props, injectedChildren: injectedChildren));
        }
      }
    }

    // If this node has a "slot" marker, inject runtime children here
    if (astJson['slot'] == 'children' && injectedChildren != null) {
      result.addAll(injectedChildren);
    }

    return result;
  }

  // ─── Node constructors ───────────────────────────────────

  static ColumnNode _columnNode(Map<String, dynamic> ast, Map<String, dynamic> props, List<WidgetNode>? injected) {
    final p = ast['props'] ?? {};
    return ColumnNode(
      children: _children(ast, props, injected),
      crossAxisAlignment: p['crossAxisAlignment'] ?? 'CrossAxisAlignment.start',
      mainAxisAlignment: p['mainAxisAlignment'] ?? 'MainAxisAlignment.start',
      mainAxisSize: p['mainAxisSize'] ?? 'MainAxisSize.min',
      spacing: (p['spacing'] as num?)?.toDouble(),
    );
  }

  static RowNode _rowNode(Map<String, dynamic> ast, Map<String, dynamic> props, List<WidgetNode>? injected) {
    final p = ast['props'] ?? {};
    return RowNode(
      children: _children(ast, props, injected),
      crossAxisAlignment: p['crossAxisAlignment'] ?? 'CrossAxisAlignment.center',
      mainAxisAlignment: p['mainAxisAlignment'] ?? 'MainAxisAlignment.start',
      spacing: (p['spacing'] as num?)?.toDouble(),
    );
  }

  static PaddingNode _paddingNode(Map<String, dynamic> ast, Map<String, dynamic> props, List<WidgetNode>? injected) {
    final p = ast['props'] ?? {};
    return PaddingNode(
      padding: _edgeInsets(p['padding']),
      child: _child(ast, props, injected),
    );
  }

  static ContainerNode _containerNode(Map<String, dynamic> ast, Map<String, dynamic> props, List<WidgetNode>? injected) {
    final p = ast['props'] ?? {};
    return ContainerNode(
      width: (p['width'] as num?)?.toDouble(),
      height: (p['height'] as num?)?.toDouble(),
      padding: p['padding'] != null ? _edgeInsets(p['padding']) : null,
      margin: p['margin'] != null ? _edgeInsets(p['margin']) : null,
      alignment: p['alignment'] != null ? AlignmentExpr(p['alignment']) : null,
      child: ast['child'] != null ? _child(ast, props, injected) : null,
    );
  }

  static ScaffoldNode _scaffoldNode(Map<String, dynamic> ast, Map<String, dynamic> props, List<WidgetNode>? injected) {
    return ScaffoldNode(
      appBar: ast['appBar'] != null
          ? interpret(Map<String, dynamic>.from(ast['appBar']), props, injectedChildren: injected)
          : null,
      body: ast['body'] != null
          ? interpret(Map<String, dynamic>.from(ast['body']), props, injectedChildren: injected)
          : null,
      backgroundColor: ast['props'] != null && ast['props']['backgroundColor'] != null
          ? _color(ast['props']['backgroundColor'])
          : null,
    );
  }

  static SizedBoxNode _sizedBoxNode(Map<String, dynamic> ast) {
    final p = ast['props'] ?? {};
    return SizedBoxNode(
      width: (p['width'] as num?)?.toDouble(),
      height: (p['height'] as num?)?.toDouble(),
    );
  }

  static TextNode _textNode(Map<String, dynamic> ast, Map<String, dynamic> props) {
    final p = ast['props'] ?? {};
    final text = _bind(p['text'] ?? '', props);
    return TextNode(
      text,
      style: p['style'] != null ? _textStyle(p['style']) : null,
    );
  }

  static IconNode _iconNode(Map<String, dynamic> ast) {
    final p = ast['props'] ?? {};
    return IconNode(
      p['icon'] ?? 'Icons.help_outline',
      size: (p['size'] as num?)?.toDouble(),
      color: p['color'] != null ? _color(p['color']) : null,
    );
  }

  static ButtonNode _buttonNode(Map<String, dynamic> ast, Map<String, dynamic> props) {
    final p = ast['props'] ?? {};
    return ButtonNode(
      text: _bind(p['text'] ?? 'Button', props),
      action: p['action'] ?? '() {}',
      variant: p['variant'] ?? 'primary',
      size: p['size'] ?? 'md',
      fullWidth: p['fullWidth'] ?? true,
    );
  }

  static TextFieldNode _textFieldNode(Map<String, dynamic> ast, Map<String, dynamic> props) {
    final p = ast['props'] ?? {};
    return TextFieldNode(
      label: _bind(p['label'] ?? 'Input', props),
      obscureText: p['obscureText'] ?? false,
    );
  }

  static SingleChildScrollViewNode _scrollNode(Map<String, dynamic> ast, Map<String, dynamic> props, List<WidgetNode>? injected) {
    final p = ast['props'] ?? {};
    return SingleChildScrollViewNode(
      padding: p['padding'] != null ? _edgeInsets(p['padding']) : null,
      child: _child(ast, props, injected),
    );
  }

  static ListViewNode _listViewNode(Map<String, dynamic> ast, Map<String, dynamic> props, List<WidgetNode>? injected) {
    final p = ast['props'] ?? {};
    return ListViewNode(
      children: _children(ast, props, injected),
      shrinkWrap: p['shrinkWrap'] ?? false,
      padding: p['padding'] != null ? _edgeInsets(p['padding']) : null,
    );
  }

  static AppBarNode _appBarNode(Map<String, dynamic> ast, Map<String, dynamic> props) {
    final p = ast['props'] ?? {};
    final List<WidgetNode> actions = [];
    if (p['actions'] != null) {
      for (var a in (p['actions'] as List)) {
        actions.add(interpret(Map<String, dynamic>.from(a), props));
      }
    }
    return AppBarNode(
      title: _bind(p['title'] ?? '', props),
      centerTitle: p['centerTitle'] ?? true,
      actions: actions,
      backgroundColor: p['backgroundColor'] != null ? _color(p['backgroundColor']) : null,
    );
  }

  static WidgetNode _listTileNode(Map<String, dynamic> ast, Map<String, dynamic> props) {
    final p = ast['props'] ?? {};
    final title = _bind(p['title'] ?? 'Item', props);
    final icon = p['icon'] ?? 'Icons.circle';
    // ListTile rendered as RawNode since there's no dedicated AST node yet
    return RawNode("ListTile(leading: Icon($icon), title: Text('$title'), trailing: const Icon(Icons.chevron_right))");
  }

  // ─── Expression helpers ──────────────────────────────────

  static EdgeInsetsExpr _edgeInsets(dynamic json) {
    if (json is num) return EdgeInsetsExpr.all(json.toDouble());
    if (json is Map) {
      final kind = json['kind'] ?? 'all';
      switch (kind) {
        case 'all':
          return EdgeInsetsExpr.all((json['value'] as num?)?.toDouble() ?? 0);
        case 'symmetric':
          return EdgeInsetsExpr.symmetric(
            horizontal: (json['horizontal'] as num?)?.toDouble(),
            vertical: (json['vertical'] as num?)?.toDouble(),
          );
        case 'only':
          return EdgeInsetsExpr.only(
            top: (json['top'] as num?)?.toDouble(),
            bottom: (json['bottom'] as num?)?.toDouble(),
            left: (json['left'] as num?)?.toDouble(),
            right: (json['right'] as num?)?.toDouble(),
          );
        case 'token':
          return EdgeInsetsExpr.token(json['token']);
      }
    }
    return const EdgeInsetsExpr.zero();
  }

  static ColorExpr _color(dynamic json) {
    if (json is String) return ColorExpr.material(json);
    if (json is Map) {
      final kind = json['kind'] ?? 'material';
      switch (kind) {
        case 'hex': return ColorExpr.hex(json['value']);
        case 'design': return ColorExpr.design(json['value']);
        default: return ColorExpr.material(json['value'] ?? 'grey');
      }
    }
    return const ColorExpr.material('grey');
  }

  static TextStyleExpr _textStyle(dynamic json) {
    if (json is! Map) return const TextStyleExpr();
    return TextStyleExpr(
      themeStyle: json['themeStyle'] as String?,
      fontSize: (json['fontSize'] as num?)?.toDouble(),
      fontWeight: json['fontWeight'] as String?,
      color: json['color'] != null ? _color(json['color']) : null,
      letterSpacing: (json['letterSpacing'] as num?)?.toDouble(),
    );
  }
}
