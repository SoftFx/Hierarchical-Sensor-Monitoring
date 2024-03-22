import {Panel} from "./dashboard.panel";
import {Dictionary} from "./dashboard.interfaces";

export class DashboardStorage{
    panels: Dictionary<Panel> = {};
    
    id: string;
    public constructor() {
        
    }
    
    public setId(id: string){
        this.id = id;
    }
    public addPanel(panel: Panel){
        this.panels[panel.id] = panel;
    }

    //panels
}
