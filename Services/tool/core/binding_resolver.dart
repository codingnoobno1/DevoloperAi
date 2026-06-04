import '../utils/string_utils.dart';

class BindingResolver {
  /// Converts a binding path like "dashboard.stats.totalRevenue" 
  /// into a Dart accessor: "context.watch<DashboardCubit>().state.stats?.totalRevenue"
  static String resolve(String path) {
    final parts = path.split('.');
    if (parts.length < 2) return "''";

    final feature = parts[0];
    final cubitName = "${StringUtils.capitalize(feature)}Cubit";
    final remainingPath = parts.skip(1).join('?.');

    return "context.watch<$cubitName>().state.$remainingPath";
  }

  /// Converts a method call like "dashboard.load" 
  /// into: "context.read<DashboardCubit>().load()"
  static String resolveAction(Map<String, dynamic> action) {
    if (action['type'] == 'navigate') {
      return "Navigator.pushNamed(context, '${action['route']}')";
    }

    if (action['type'] == 'call') {
      final methodPath = action['method'] as String?;
      if (methodPath == null) return "";

      final parts = methodPath.split('.');
      if (parts.length < 2) return "";

      final feature = parts[0];
      final cubitName = "${StringUtils.capitalize(feature)}Cubit";
      final methodName = parts[1];

      return "context.read<$cubitName>().$methodName()";
    }

    return "";
  }
}
