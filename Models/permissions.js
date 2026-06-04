const mongoose = require('mongoose');
const { Schema } = mongoose;

const PermissionSchema = new Schema({
  role: {
    type: String,
    enum: ["project_master", "developer", "rnd_associate", "ai_agent"],
    required: true,
  },
  panel: {
    type: String,
    required: true,
  },
  accessLevel: {
    type: String,
    enum: ["none", "view", "edit", "full"],
    default: "none",
  },
  allowedActions: [String], // e.g. ["create", "read", "update", "delete"]
});

// Ensure role and panel are unique together
PermissionSchema.index({ role: 1, panel: 1 }, { unique: true });

module.exports = mongoose.model('Permission', PermissionSchema);
