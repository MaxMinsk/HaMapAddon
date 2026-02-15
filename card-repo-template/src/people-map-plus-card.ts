import { LitElement, css, html } from "lit";

type Hass = {
  states: Record<string, unknown>;
  callApi?: (method: string, path: string, parameters?: unknown) => Promise<unknown>;
};

type PersonConfig = {
  entity: string;
  tracker?: string;
  avatar?: string;
  icon?: string;
  color?: string;
  label?: "name" | "entity";
};

type CardConfig = {
  type: "custom:people-map-plus";
  persons?: PersonConfig[];
  map?: {
    hours?: number;
    show_zones?: boolean;
  };
  layers?: {
    track?: boolean;
    stops?: boolean;
    photos?: boolean;
  };
};

class PeopleMapPlusCard extends LitElement {
  public hass?: Hass;
  private _config?: CardConfig;

  static styles = css`
    :host {
      display: block;
    }
    ha-card {
      padding: 16px;
    }
    .title {
      font-size: 18px;
      font-weight: 600;
      margin-bottom: 8px;
    }
    .meta {
      color: var(--secondary-text-color);
      font-size: 13px;
    }
  `;

  public setConfig(config: CardConfig): void {
    if (!config || config.type !== "custom:people-map-plus") {
      throw new Error("Invalid config for custom:people-map-plus");
    }
    this._config = config;
  }

  public getCardSize(): number {
    return 3;
  }

  protected render() {
    if (!this._config) {
      return html`<ha-card><div>Card is not configured.</div></ha-card>`;
    }

    const personCount = this._config.persons?.length ?? 0;
    const hours = this._config.map?.hours ?? 6;
    return html`
      <ha-card>
        <div class="title">People Map Plus</div>
        <div class="meta">MVP scaffold. Persons: ${personCount}. Hours: ${hours}.</div>
      </ha-card>
    `;
  }
}

customElements.define("people-map-plus-card", PeopleMapPlusCard);

declare global {
  interface Window {
    customCards?: Array<{
      type: string;
      name: string;
      description: string;
    }>;
  }
}

window.customCards = window.customCards || [];
window.customCards.push({
  type: "people-map-plus-card",
  name: "People Map Plus",
  description: "Extended people tracking map card (scaffold)"
});

export { PeopleMapPlusCard };

