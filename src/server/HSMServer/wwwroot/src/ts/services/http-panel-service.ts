import {Panel} from "../dashboard.panel";


export class HttpPanelService {
    async updateSettings(panel: Panel) {
        return fetch(window.location.pathname + `/${panel.id}`, {
            method: 'PUT',
            headers: {
                "Content-Type": "application/json",
            },
            body: JSON.stringify(panel.settings)
        });
    }
    
    async getPanel(panel: Panel): Promise<string>{
        const result = await fetch(window.location.pathname + `/${panel.id}/Switch`, {
            method: 'get',
            headers: {
                "Content-Type": "application/json",
            },
        });
        
        return result.text()
    }
}