// users.js
const mongoose = require("mongoose");
const { Schema } = mongoose;

const UserSchema = new Schema({
  name: { type: String, required: true },
  email: { type: String, unique: true, required: true },
  passwordHash: { type: String, required: true },

  role: {
    type: String,
    enum: ["developer", "rnd_associate", "project_master"],
    required: true,
  },

  skills: [String],
  assignedProjects: [{ type: Schema.Types.ObjectId, ref: "Project" }],
  activeTasks: [{ type: Schema.Types.ObjectId, ref: "Task" }],
  aiPerformanceScore: { type: Number, default: 0 }, // AI-evaluated efficiency metric
  lastActive: { type: Date, default: Date.now },
});

module.exports = mongoose.model("User", UserSchema);
