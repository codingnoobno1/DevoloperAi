const mongoose = require('mongoose');
const { Schema } = mongoose;

const SyncEventSchema = new Schema({
  eventType: {
    type: String,
    enum: ['git_push', 'task_update', 'project_sync', 'ai_log'],
    required: true,
  },
  status: {
    type: String,
    enum: ['success', 'failed', 'pending'],
    required: true,
  },
  source: {
    type: String,
    enum: ['express_api', 'asp_net_agent'],
    required: true,
  },
  message: {
    type: String,
  },
  payload: {
    type: Schema.Types.Mixed,
  },
  timestamp: {
    type: Date,
    default: Date.now,
  },
});

module.exports = mongoose.model('SyncEvent', SyncEventSchema);
