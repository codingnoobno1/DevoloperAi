import '../core/file_writer.dart';
import '../core/models.dart';
import 'base_generator.dart';

class AiGenerator implements BaseGenerator {
  @override
  Future<void> generate(AppModel model) async {
    final code = """
class AIService {
  Future<String> processPrompt(String prompt) async {
    print("AI Processing: \\\$prompt");
    return "AI Response to: \\\$prompt";
  }
}
""";
    FileWriter.write('lib_gen/core/services/ai_service.dart', code);
  }
}
