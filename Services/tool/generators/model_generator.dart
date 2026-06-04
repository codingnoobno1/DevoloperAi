import '../core/file_writer.dart';
import '../core/models.dart';
import 'base_generator.dart';

class ModelGenerator implements BaseGenerator {
  @override
  Future<void> generate(AppModel model) async {
    model.models.forEach((modelName, fields) {
      String fieldDeclarations = "";
      String constructorParams = "";
      String fromJsonMap = "";
      String toJsonMap = "";

      fields.forEach((fieldName, type) {
        final dartType = type == 'string' ? 'String' : (type == 'int' ? 'int' : 'dynamic');
        fieldDeclarations += "  final $dartType $fieldName;\n";
        constructorParams += "    required this.$fieldName,\n";
        fromJsonMap += "      $fieldName: json['$fieldName'],\n";
        toJsonMap += "      '$fieldName': $fieldName,\n";
      });

      final code = """
class $modelName {
$fieldDeclarations
  $modelName({
$constructorParams  });

  factory $modelName.fromJson(Map<String, dynamic> json) => $modelName(
$fromJsonMap  );

  Map<String, dynamic> toJson() => {
$toJsonMap  };
}
""";
      FileWriter.write('lib_gen/core/models/${modelName.toLowerCase()}.dart', code);
    });
  }
}
