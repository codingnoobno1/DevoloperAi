const mongoose = require('mongoose');
const { Schema } = mongoose;

const ProjectSchema = new Schema({
  projectName: {
    type: String,
    required: true,
  },
  description: {
    type: String,
  },
  projectMaster: {
    type: Schema.Types.ObjectId,
    ref: 'User',
    required: true,
  },
  developers: [{
    type: Schema.Types.ObjectId,
    ref: 'User',
  }],
  pendingDevelopers: [{
    type: Schema.Types.ObjectId,
    ref: 'User',
  }],
  status: {
    type: String,
    enum: ['planning', 'in_progress', 'completed', 'on_hold'],
    default: 'planning',
  },
  tasks: [{
    type: Schema.Types.ObjectId,
    ref: 'Task',
  }],
}, { timestamps: true });

module.exports = mongoose.model('Project', ProjectSchema);
