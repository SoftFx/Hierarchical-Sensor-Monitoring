import {PanelSettings} from "../dashboard.classes";

export class HttpPanelService {
    async updateSettings(settings: PanelSettings) {
        return await fetch(window.location.pathname + "/Panels", {
            method: 'PUT',
            headers: {
                "Content-Type": "application/json",
            },
            body: JSON.stringify(settings)
        });
    }
}