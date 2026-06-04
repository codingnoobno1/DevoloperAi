import 'dart:convert';
import 'dart:io';

void main() {
  print("Generating files in /lib...");

  final jsonStr = File('config/screentype.json').readAsStringSync();
  final data = jsonDecode(jsonStr);

  generateScreens(data);
  generateRoutes(data);

  print("Done ✅");
}

void generateScreens(Map<String, dynamic> screens) {
  for (var entry in screens.entries) {
    final name = entry.key;

    final className = "${capitalize(name)}Screen";

    final code = '''
import 'package:flutter/material.dart';

class $className extends StatelessWidget {
  const $className({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text("$name")),
      body: const Center(child: Text("$name screen")),
    );
  }
}
''';

    final file = File('lib/screens/${name}_screen.dart');
    file.createSync(recursive: true);
    file.writeAsStringSync(code);
  }
}

void generateRoutes(Map<String, dynamic> screens) {
  String routes = '''
import 'package:flutter/material.dart';
''';

  for (var entry in screens.entries) {
    final name = entry.key;
    routes += "import '../screens/${name}_screen.dart';\n";
  }

  routes += '\nMap<String, WidgetBuilder> appRoutes = {\n';

  for (var entry in screens.entries) {
    final name = entry.key;
    final route = entry.value['route'];
    final className = "${capitalize(name)}Screen";

    routes += "  '$route': (context) => const $className(),\n";
  }

  routes += '};';

  final file = File('lib/routes/app_routes.dart');
  file.createSync(recursive: true);
  file.writeAsStringSync(routes);
}

String capitalize(String s) {
  if (s.isEmpty) return s;
  return s[0].toUpperCase() + s.substring(1);
}
