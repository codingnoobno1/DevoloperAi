import '../core/file_writer.dart';
import '../core/models.dart';
import 'base_generator.dart';

class DbGenerator implements BaseGenerator {
  @override
  Future<void> generate(AppModel model) async {
    final dbConfig = model.db;
    final dbType = dbConfig['database']?['type'] ?? 'sqlite';
    final dbCode = """
class DbService {
  final String dbType = "$dbType";

  Future<void> init() async {
    print("Initializing \\\$dbType database...");
  }
}
""";
    FileWriter.write('lib_gen/core/services/db_service.dart', dbCode);
  }
}
