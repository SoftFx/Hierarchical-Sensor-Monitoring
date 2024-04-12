import {Dictionary, IPanelSettings} from "./dashboard.interfaces";
import * as moment from "moment";
import {Layout} from "./plotUpdate";
import {PanelSettings} from "./dashboard.classes";
import {HttpPanelService} from "./services/http-panel-service";
import {Hovermode} from "./types";

export const httpPanelService : HttpPanelService = new HttpPanelService();

export class DashboardStorage {
    private _intervalId: number;
    private panels: Dictionary<Panel> = {};

    id: string;

    public constructor() {
        this._intervalId = this.checkForUpdate(this.panels);
    }

    public addPanel(panel: Panel) {
        this.panels[panel.id] = panel;
        this.panels[panel.id].updateNotify();

        window.clearInterval(this._intervalId)
        this._intervalId = this.checkForUpdate(this.panels);
    }

    public getPanel(id: string): Panel {
        return this.panels[id];
    }
    
    checkForUpdate(panels: Dictionary<Panel>) {
        return window.setInterval(function () {
            Object.values(panels).forEach(panel => {
                panel.updateNotify();
            });
        }, 5000);
    }
}

export class Panel {
    private _lastUpdateTime: Date = new Date(0);
    private _lastUpdateDiv: JQuery<HTMLElement>;

    private _savebutton: JQuery<HTMLElement>;
    
    
    id: string
    settings: PanelSettings



    constructor(id: string, settings: IPanelSettings) {
        this.id = id;
        this._lastUpdateDiv = $('#lastUpdate_' + this.id);
        
        this.settings = new PanelSettings(this.id, settings);

        $('#selecthovermode_' + id).val(this.settings.hovermode as string);
        $('#hoverdistance_' + id).val(this.settings.hoverDistance);
        
        Layout.relayout(this.id, this.settings);

        this._savebutton = $('#button_save_settings_' + id);
        this._savebutton.on('click', async function (){
            this.settings.hovermode = $('#selecthovermode_' + id).val() as Hovermode;
            this.settings.hoverDistance = $('#hoverdistance_' + id).val() as number;
            
            await httpPanelService.updateSettings(this.settings);
            Layout.relayout(this.id, this.settings);
            $('#actionButton').trigger('click')
        }.bind(this))
        
        this.updateNotify();
    }
    
    get lastUpdateTime(): Date {
        return this._lastUpdateTime;
    }

    set lastUpdateTime(time: Date) {
        if (time > this._lastUpdateTime) {
            this._lastUpdateTime = time;
            this.updateNotify();
        }
    }

    updateNotify() {
        if (this._lastUpdateTime.getTime() === 0)
            this._lastUpdateDiv.html("Never updated");
        else
            this._lastUpdateDiv.html(moment(this._lastUpdateTime).fromNow());
    }
}