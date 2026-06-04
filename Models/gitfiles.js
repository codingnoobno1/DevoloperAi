const mongoose = require('mongoose');
const { Schema } = mongoose;

const GitFileSchema = new Schema({
  task: { type: Schema.Types.ObjectId, ref: "Task" },
  filePath: String,
  branch: String,
  commitHash: String,

  author: { type: Schema.Types.ObjectId, ref: "User" },
  lastUpdated: { type: Date, default: Date.now },

  syncStatus: {
    type: String,
    enum: ["pending", "synced", "conflict"],
    default: "pending",
  },

  aiReviewed: { type: Boolean, default: false },
});

module.exports = mongoose.model('GitFile', GitFileSchema);
