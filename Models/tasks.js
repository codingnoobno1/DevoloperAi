const mongoose = require('mongoose');
const { Schema } = mongoose;

const TaskSchema = new Schema({
  title: String,
  description: String,
  project: { type: Schema.Types.ObjectId, ref: "Project", required: true },

  assignedTo: { type: Schema.Types.ObjectId, ref: "User" },
  reviewedBy: { type: Schema.Types.ObjectId, ref: "User" },
  
  metadata: {
    type: { type: String, enum: ['development', 'research', 'review'] },
    researchCategory: String,
    findings: [{
      date: { type: Date, default: Date.now },
      notes: String,
      attachments: [String]
    }],
    benchmarks: [{
      metric: String,
      value: Number,
      date: { type: Date, default: Date.now }
    }]
  },

  files: [{ type: Schema.Types.ObjectId, ref: "GitFile" }],
  dependencies: [{ type: Schema.Types.ObjectId, ref: "Task" }], // Linked subtasks
  aiSuggestions: [{ type: Schema.Types.ObjectId, ref: "AiLog" }],

  startDate: Date,
  deadline: Date,
  completedAt: Date,

  status: {
    type: String,
    enum: ["not_started", "in_progress", "under_review", "done"],
    default: "not_started",
  },
});

module.exports = mongoose.model('Task', TaskSchema);
