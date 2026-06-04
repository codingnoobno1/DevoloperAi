import '../core/file_writer.dart';
import '../core/models.dart';
import 'base_generator.dart';

class NavigationServiceGenerator implements BaseGenerator {
  @override
  Future<void> generate(AppModel model) async {
    final code = """
import 'package:flutter/material.dart';

class NavigationService {
  static final GlobalKey<NavigatorState> navigatorKey = GlobalKey<NavigatorState>();

  static Future<dynamic> navigateTo(String routeName) {
    return navigatorKey.currentState!.pushNamed(routeName);
  }

  static void goBack() {
    return navigatorKey.currentState!.pop();
  }
}
""";
    FileWriter.write('lib_gen/core/navigation/navigation_service.dart', code);
  }
}
