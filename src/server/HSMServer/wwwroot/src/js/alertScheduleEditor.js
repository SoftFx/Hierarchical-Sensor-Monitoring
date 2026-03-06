import { EditorView, basicSetup } from 'codemirror';
import { EditorState } from '@codemirror/state';
import { yaml } from '@codemirror/lang-yaml';
import { syntaxHighlighting, defaultHighlightStyle } from '@codemirror/language';
import { autocompletion } from '@codemirror/autocomplete';
import { linter } from '@codemirror/lint';
import { yamlSchema, yamlCompletion } from 'codemirror-json-schema/yaml';
import yamlParser from 'js-yaml';


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


const yamlSyntaxLinter = linter(async (view) => {
    const text = view.state.doc.toString();
    const diagnostics = [];

    try {
        yamlParser.load(text); 
    } catch (e) {
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


window.checkYamlErrors = function () {
    if (window.scheduleEditor) {
        try {
            yamlParser.load(window.scheduleEditor.state.doc.toString());
            return false; 
        } catch (e) {
            return true; 
        }
    }
    return true; 
};

export function initAlertScheduleEditor(textareaId, containerId) {
    const textarea = document.getElementById(textareaId);
    const container = document.getElementById(containerId);

    if (!textarea || !container) {
        console.warn('Textarea or container not found for AlertScheduleEditor');
        return;
    }

    const initialYaml = textarea.value;

    const startState = EditorState.create({
        doc: initialYaml,
        extensions: [
            basicSetup,
            syntaxHighlighting(defaultHighlightStyle),
            yaml(),
            autocompletion({
                override: [yamlCompletion(scheduleSchema)]
            }),
            yamlSyntaxLinter,
            yamlSchema(scheduleSchema),

            EditorView.updateListener.of((update) => {
                if (update.docChanged && window.onScheduleYamlChange) {
                    window.onScheduleYamlChange();
                }
            })
        ]
    });

    const editor = new EditorView({
        state: startState,
        parent: container
    });


    window.scheduleEditor = editor;

    textarea.style.display = 'none';

    const form = textarea.closest('form');
    if (form) {
        form.addEventListener('submit', () => {
            textarea.value = editor.state.doc.toString();
        });
    }
}