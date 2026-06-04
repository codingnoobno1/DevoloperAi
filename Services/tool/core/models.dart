class AppModel {
  final List<ScreenModel> screens;
  final Map<String, LayoutModel> layouts;
  final Map<String, WidgetModel> widgets;
  final NavigationModel navigation;
  final List<Map<String, dynamic>> models;
  final Map<String, dynamic> datasource;
  final Map<String, dynamic> theme;
  final Map<String, dynamic> bloc;
  final Map<String, dynamic> db;
  final Map<String, dynamic> state;
  final Map<String, dynamic> repository;
  final Map<String, dynamic> condition;
  final Map<String, dynamic> api;

  AppModel({
    required this.screens,
    required this.layouts,
    required this.widgets,
    required this.navigation,
    required this.models,
    required this.datasource,
    required this.theme,
    required this.bloc,
    required this.db,
    required this.state,
    required this.repository,
    required this.condition,
    required this.api,
  });

  factory AppModel.fromJson(Map<String, dynamic> json) {
    final Map<String, LayoutModel> extractedLayouts = {};
    final screensList = (json['screens'] as List?)?.map((e) {
      final screenMap = Map<String, dynamic>.from(e);
      if (screenMap['layout'] is Map) {
        final layoutId = "${screenMap['id'] ?? screenMap['name']}_layout";
        extractedLayouts[layoutId] = LayoutModel.fromJson(Map<String, dynamic>.from(screenMap['layout']));
        screenMap['layout'] = layoutId;
      }
      return ScreenModel.fromJson(screenMap);
    }).toList() ?? [];

    final Map<String, WidgetModel> widgetsMap = {};
    final widgetsData = json['widgets'];
    if (widgetsData is List) {
      for (var w in widgetsData) {
        final wMap = Map<String, dynamic>.from(w);
        final id = wMap['id'] ?? wMap['name'];
        widgetsMap[id] = WidgetModel.fromJson(wMap);
      }
    } else if (widgetsData is Map) {
      widgetsData.forEach((k, v) {
        widgetsMap[k as String] = WidgetModel.fromJson(Map<String, dynamic>.from(v));
      });
    }

    return AppModel(
      screens: screensList,
      layouts: {
        ...extractedLayouts,
        ...(json['layouts'] as Map?)?.map((k, v) => MapEntry(k as String, LayoutModel.fromJson(Map<String, dynamic>.from(v)))) ?? {}
      },
      widgets: widgetsMap,
      navigation: NavigationModel.fromJson(Map<String, dynamic>.from(json['navigation'] ?? json['nav'] ?? {})),
      models: (json['models'] as List?)?.map((e) => Map<String, dynamic>.from(e)).toList() ?? [],
      datasource: Map<String, dynamic>.from(json['datasource'] ?? {}),
      theme: Map<String, dynamic>.from(json['theme'] ?? {}),
      bloc: Map<String, dynamic>.from(json['bloc'] ?? {}),
      db: Map<String, dynamic>.from(json['db'] ?? {}),
      state: Map<String, dynamic>.from(json['state'] ?? {}),
      repository: Map<String, dynamic>.from(json['repository'] ?? {}),
      condition: Map<String, dynamic>.from(json['condition'] ?? {}),
      api: Map<String, dynamic>.from(json['api'] ?? {}),
    );
  }
}

class ScreenModel {
  final String id;
  String get name => id; 
  final String layout;
  String? type;
  final Map<String, dynamic> data;

  ScreenModel({
    required this.id,
    required this.layout,
    this.type,
    required this.data,
  });

  factory ScreenModel.fromJson(Map<String, dynamic> json) {
    return ScreenModel(
      id: json['id'] ?? json['name'] ?? '',
      layout: json['layout'] is String ? json['layout'] : '',
      type: json['type'],
      data: Map<String, dynamic>.from(json['data'] ?? {"type": "static"}),
    );
  }
}

class NavigationModel {
  final String entry;
  final Map<String, dynamic> flow; // Restored
  final List<Map<String, dynamic>> routesList;
  Map<String, String> get routes {
    final Map<String, String> map = {};
    for (var r in routesList) {
      if (r['id'] != null && r['to'] != null) {
        map[r['id'].toString()] = r['to'].toString();
      }
    }
    return map;
  }

  NavigationModel({required this.entry, required this.flow, required this.routesList});

  factory NavigationModel.fromJson(Map<String, dynamic> json) {
    final routesRaw = json['routes'];
    final List<Map<String, dynamic>> list = [];
    
    if (routesRaw is List) {
      for (var r in routesRaw) list.add(Map<String, dynamic>.from(r));
    } else if (routesRaw is Map) {
      routesRaw.forEach((k, v) => list.add({"id": k, "to": v}));
    }

    return NavigationModel(
      entry: json['entry'] ?? 'home',
      flow: Map<String, dynamic>.from(json['flow'] ?? {}),
      routesList: list,
    );
  }
}

class LayoutModel {
  final String type;
  final String? variant; // Restored
  final String? template;
  final List<String> children;
  final List<Map<String, dynamic>> slots;
  final Map<String, dynamic> props;
  
  final Map<String, dynamic>? ast = null;
  final List<String> tabs = [];

  LayoutModel({
    required this.type,
    this.variant,
    this.template,
    required this.children,
    required this.slots,
    required this.props,
  });

  factory LayoutModel.fromJson(Map<String, dynamic> json) {
    final slotsRaw = json['slots'] as List?;
    final List<Map<String, dynamic>> slotsList = [];
    final List<String> childKeys = [];

    if (slotsRaw != null) {
      for (var s in slotsRaw) {
        final sMap = Map<String, dynamic>.from(s);
        slotsList.add(sMap);
        if (sMap.containsKey('id')) childKeys.add(sMap['id']);
      }
    }

    return LayoutModel(
      type: json['type'] ?? 'Column',
      variant: json['variant'],
      template: json['template'],
      children: childKeys.isNotEmpty ? childKeys : List<String>.from(json['children'] ?? []),
      slots: slotsList,
      props: Map<String, dynamic>.from(json['props'] ?? {}),
    );
  }
}

class WidgetModel {
  final String id;
  String get name => id; 
  final String type;
  final String? variant; // Restored
  final String? template;
  final Map<String, dynamic> data;
  final Map<String, dynamic> properties;
  final Map<String, dynamic>? action;
  final Map<String, dynamic>? animation; // Restored
  
  final Map<String, dynamic>? ast = null;
  List<WidgetModel> get children => []; // Restored
  List<String> get childKeys => [];

  WidgetModel({
    required this.id,
    required this.type,
    this.variant,
    this.template,
    required this.data,
    required this.properties,
    this.action,
    this.animation,
  });

  factory WidgetModel.fromJson(Map<String, dynamic> json) {
    return WidgetModel(
      id: json['id'] ?? json['name'] ?? '',
      type: json['type'] ?? 'Container',
      variant: json['variant'],
      template: json['template'],
      data: Map<String, dynamic>.from(json['data'] ?? {"type": "static"}),
      properties: Map<String, dynamic>.from(json['properties'] ?? json['props'] ?? {}),
      action: json['action'] as Map<String, dynamic>?,
      animation: json['animation'] as Map<String, dynamic>?,
    );
  }
}
