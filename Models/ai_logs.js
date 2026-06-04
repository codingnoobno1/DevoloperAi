const mongoose = require('mongoose');
const { Schema } = mongoose;

const AiLogSchema = new Schema({
  project: { type: Schema.Types.ObjectId, ref: "Project" },
  task: { type: Schema.Types.ObjectId, ref: "Task" },
  triggeredBy: {
    type: String,
    enum: ["system", "user", "agent"],
    required: true,
  },
  actionType: {
    type: String,
    enum: ["auto_commit", "review", "insight", "alert"],
    required: true,
  },
  message: {
    type: String,
    required: true,
  },
  confidence: {
    type: Number,
  },
  timestamp: {
    type: Date,
    default: Date.now,
  },
});

module.exports = mongoose.model('AiLog', AiLogSchema);