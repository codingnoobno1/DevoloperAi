import 'models.dart';
import 'dependency_graph.dart';
import 'ast.dart';

/// ProjectIR v6.5: Deep Intermediate Representation with Build Safety.
/// Now includes BuildStatus tracking to prevent caching of invalid states.
class ProjectIR {
  final Map<String, FeatureIR> features = {};
  final Map<String, ServiceIR> services = {};
  final ThemeIR theme = ThemeIR();
  final OptimizationStats optimization = OptimizationStats();
  final BuildStatus buildStatus = BuildStatus();
  final List<String> errors = [];
  
  Map<String, dynamic> metadata = {};

  void addError(String msg) => errors.add(msg);
  bool get hasErrors => errors.isNotEmpty;
  
  /// The IR is only valid if there are zero errors.
  bool get isValid => !hasErrors;
}

class BuildStatus {
  bool success = false;
  String? failedPhase;
  DateTime? timestamp;

  Map<String, dynamic> toJson() => {
    "success": success,
    "failed_phase": failedPhase,
    "timestamp": timestamp?.toIso8601String(),
  };
}

class FeatureIR {
  final String name;
  final List<ScreenIR> screens = [];
  final Map<String, WidgetNode> widgetASTs = {};
  final Map<String, dynamic> bloc;
  final Map<String, dynamic> repository;

  FeatureIR(this.name, {required this.bloc, required this.repository});
}

class ScreenIR {
  final String name;
  final String route;
  final String type; // main, tab, sub
  late WidgetNode root;

  ScreenIR(this.name, this.route, this.type);
}

class PropertyIR {
  final String key;
  final dynamic value;
  final String type;
  final bool isDynamic;

  PropertyIR(this.key, this.value, this.type, {this.isDynamic = false});
}

class ActionIR {
  final String type;
  final Map<String, dynamic> params;
  final List<String> preconditions;

  ActionIR(this.type, this.params, {this.preconditions = const []});
}

class ServiceIR {
  final String name;
  final String type;
  final Map<String, dynamic> config;

  ServiceIR(this.name, this.type, this.config);
}

class ThemeIR {
  Map<String, dynamic> tokens = {};
  Map<String, dynamic> components = {};
  Map<String, dynamic> lightColors = {};
  Map<String, dynamic> darkColors = {};
  
  bool isValid = false;
}

class OptimizationStats {
  int redundantWidgetsRemoved = 0;
  int layoutNodesFlattened = 0;
  int propertiesInlined = 0;
  List<String> logs = [];

  void log(String msg) => logs.add(msg);
  
  Map<String, dynamic> toJson() => {
    "widgets_removed": redundantWidgetsRemoved,
    "nodes_flattened": layoutNodesFlattened,
    "properties_inlined": propertiesInlined,
    "logs": logs,
  };
}
