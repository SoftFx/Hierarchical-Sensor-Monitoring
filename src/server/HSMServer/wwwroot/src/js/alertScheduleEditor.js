// Import necessary modules from CodeMirror 6 and related libraries
import { EditorView, basicSetup } from 'codemirror';
import { EditorState } from '@codemirror/state';
import { yaml } from '@codemirror/lang-yaml';
import { syntaxHighlighting, defaultHighlightStyle } from '@codemirror/language';
import { autocompletion } from '@codemirror/autocomplete';
import { linter } from '@codemirror/lint';
import { yamlSchema, yamlCompletion } from 'codemirror-json-schema/yaml';
import yamlParser from 'js-yaml';

// ============================================================
// 1. Define the JSON schema for YAML validation
//    This schema describes the expected structure of the YAML.
// ============================================================
const scheduleSchema = {
    type: "object",
    additionalProperties: false,
    properties: {
        timezone: { type: "string", description: "Timezone (e.g., Europe/Minsk)" },
        daySchedules: {
            type: "array",
            items: {
                type: "object",
                additionalProperties: false,
                properties: {
                    days: {
                        type: "array",
                        items: { type: "string", enum: ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"] }
                    },
                    windows: {
                        type: "array",
                        items: {
                            type: "object",
                            additionalProperties: false,
                            properties: {
                                start: { type: "string", pattern: "^([01]?[0-9]|2[0-3]):[0-5][0-9]$" },
                                end: { type: "string", pattern: "^([01]?[0-9]|2[0-3]):[0-5][0-9]$" }
                            },
                            required: ["start", "end"]
                        }
                    }
                },
                required: ["days", "windows"]
            }
        },
        disabledDates: {
            type: "array",
            items: { type: "string", pattern: "^\\d{4}-\\d{2}-\\d{2}$" }
        },
        overrides: {
            type: "object",
            additionalProperties: false,
            properties: {
                enabledDates: {
                    type: "array",
                    items: { type: "string", pattern: "^\\d{4}-\\d{2}-\\d{2}$" }
                },
                disabledDates: {
                    type: "array",
                    items: { type: "string", pattern: "^\\d{4}-\\d{2}-\\d{2}$" }
                },
                customScheduleDates: {
                    type: "array",
                    items: {
                        type: "object",
                        additionalProperties: false,
                        properties: {
                            date: { type: "string", pattern: "^\\d{4}-\\d{2}-\\d{2}$" },
                            scheduleType: { type: "string", enum: ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"] },
                            windows: {
                                type: "array",
                                items: {
                                    type: "object",
                                    additionalProperties: false,
                                    properties: {
                                        start: { type: "string", pattern: "^([01]?[0-9]|2[0-3]):[0-5][0-9]$" },
                                        end: { type: "string", pattern: "^([01]?[0-9]|2[0-3]):[0-5][0-9]$" }
                                    },
                                    required: ["start", "end"]
                                }
                            }
                        },
                        required: ["date"],
                        oneOf: [
                            { required: ["scheduleType"] },
                            { required: ["windows"] }
                        ]
                    }
                }
            }
        }
    },
    required: ["daySchedules"]
};

// ============================================================
// 2. YAML syntax linter (checks for basic YAML errors)
// ============================================================
const yamlSyntaxLinter = linter(async (view) => {
    const text = view.state.doc.toString();
    const diagnostics = [];

    try {
        yamlParser.load(text); // try to parse the YAML
    } catch (e) {
        // Extract error position if available (js-yaml provides e.mark)
        let from = 0;
        let to = 0;
        if (e.mark) {
            from = e.mark.position;
            to = from + 1;
        }
        diagnostics.push({
            from,
            to: to || from + 1,
            severity: 'error',
            message: `YAML error: ${e.message.replace(/^YAMLException:\s*/, '')}`
        });
    }
    return diagnostics;
});

// ============================================================
// 3. Main function to initialize the editor on a given page
// ============================================================
/**
 * Initializes a CodeMirror editor for YAML editing.
 * @param {string} textareaId - The id of the original <textarea> element (will be hidden).
 * @param {string} containerId - The id of the div where the editor will be mounted.
 */
export function initAlertScheduleEditor(textareaId, containerId) {
    const textarea = document.getElementById(textareaId);
    const container = document.getElementById(containerId);

    if (!textarea || !container) {
        console.warn('Textarea or container not found for AlertScheduleEditor');
        return;
    }

    // Use the current value of the textarea as initial document
    const initialYaml = textarea.value;

    // Create the editor state with all desired extensions
    const startState = EditorState.create({
        doc: initialYaml,
        extensions: [
            basicSetup,                           // includes line numbers, history, etc.
            syntaxHighlighting(defaultHighlightStyle), // default syntax highlighting
            yaml(),                                 // YAML language support
            autocompletion({
                override: [
                    yamlCompletion(scheduleSchema) // schema‑based suggestions for fields
                ]
            }),
            yamlSyntaxLinter,                       // YAML syntax checker
            yamlSchema(scheduleSchema)               // JSON schema validation
        ]
    });

    // Create the editor view inside the container
    const editor = new EditorView({
        state: startState,
        parent: container
    });

    // Hide the original textarea (it will still hold the value for submission)
    textarea.style.display = 'none';

    // Before the form is submitted, copy the editor content back to the textarea
    const form = textarea.closest('form');
    if (form) {
        form.addEventListener('submit', () => {
            textarea.value = editor.state.doc.toString();
        });
    }
}