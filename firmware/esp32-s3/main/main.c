#include <stdio.h>
#include <string.h>
#include <stdbool.h>
#include <stdint.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_err.h"
#include "driver/i2c_master.h"
#include "driver/usb_serial_jtag.h"

#define FADER_ADDR      0x20
#define SDA_PIN         GPIO_NUM_8
#define SCL_PIN         GPIO_NUM_9
#define REG_STATE       0x01
#define REG_SELF_CAL    0x07
#define REG_LAYER       0x0D
#define REG_TARGET      0x0E
#define REG_FW_VERSION  0x11

static i2c_master_dev_handle_t fader;

static void send_reply(const char *message)
{
    usb_serial_jtag_write_bytes(message, strlen(message), pdMS_TO_TICKS(500));
    usb_serial_jtag_write_bytes("\n", 1, pdMS_TO_TICKS(500));
}

static esp_err_t read_register(uint8_t reg, uint8_t *out, size_t size)
{
    return i2c_master_transmit_receive(fader, &reg, 1, out, size, 100);
}

static esp_err_t send_register(const uint8_t *buf, size_t size)
{
    return i2c_master_transmit(fader, buf, size, 100);
}

typedef struct {
    uint8_t mode;
    uint8_t layer;
    uint8_t position;
    bool touch;
} fader_state_t;

static esp_err_t read_fader_state(fader_state_t *state)
{
    uint8_t b[4] = {0};
    esp_err_t err = read_register(REG_STATE, b, sizeof(b));
    if (err != ESP_OK) return err;
    uint32_t raw = ((uint32_t)b[0] << 24) | ((uint32_t)b[1] << 16)
                 | ((uint32_t)b[2] << 8) | (uint32_t)b[3];
    state->touch = (raw & 0x1u) != 0;
    state->mode = (raw >> 1) & 0x7u;
    state->layer = (raw >> 4) & 0x7u;
    state->position = (raw >> 7) & 0xffu;
    return ESP_OK;
}

static bool safe_for_manual_command(void)
{
    fader_state_t state;
    if (read_fader_state(&state) != ESP_OK) {
        send_reply("ERR I2C_READ");
        return false;
    }
    if (state.touch || state.mode == 1 || state.mode == 4 || state.mode == 3) {
        send_reply("ERR FADER_BUSY_OR_TOUCHED");
        return false;
    }
    return true;
}

static void handle_command(const char *line)
{
    if (strcmp(line, "PING") == 0) {
        send_reply("PONG 1");
    } else if (strcmp(line, "STATE") == 0) {
        fader_state_t state;
        if (read_fader_state(&state) != ESP_OK) {
            send_reply("ERR I2C_READ");
            return;
        }
        char reply[64];
        snprintf(reply, sizeof(reply), "STATE %u %u %u %u",
                 state.mode, state.layer, state.position, state.touch ? 1u : 0u);
        send_reply(reply);
    } else if (strncmp(line, "MOVE ", 5) == 0) {
        unsigned layer = 0, pos = 0, speed = 0;
        char extra;
        if (sscanf(line, "MOVE %u %u %u %c", &layer, &pos, &speed, &extra) != 3 ||
            layer > 7 || pos > 255 || speed > 255) {
            send_reply("ERR INVALID_MOVE");
            return;
        }
        if (!safe_for_manual_command()) return;
        uint8_t ver[2];
        if (read_register(REG_FW_VERSION, ver, 2) != ESP_OK) {
            send_reply("ERR FW_VERSION");
            return;
        }
        uint16_t version = ((uint16_t)ver[0] << 8) | ver[1];
        if (version == 0xffffu || version == 0) {
            send_reply("ERR UNSUPPORTED_FW_VERSION");
            return;
        }
        uint8_t msg[4] = {REG_TARGET, (uint8_t)layer, (uint8_t)pos, (uint8_t)speed};
        size_t len = (version >= 0x0101u) ? 4u : 3u;
        if (send_register(msg, len) != ESP_OK) {
            send_reply("ERR I2C_WRITE");
            return;
        }
        send_reply("OK MOVE");
    } else if (strncmp(line, "LAYER ", 6) == 0) {
        unsigned layer = 0;
        char extra;
        if (sscanf(line, "LAYER %u %c", &layer, &extra) != 1 || layer > 7) {
            send_reply("ERR INVALID_LAYER");
            return;
        }
        if (!safe_for_manual_command()) return;
        uint8_t msg[2] = {REG_LAYER, (uint8_t)layer};
        if (send_register(msg, sizeof(msg)) != ESP_OK) {
            send_reply("ERR I2C_WRITE");
            return;
        }
        send_reply("OK LAYER");
    } else if (strcmp(line, "CALIBRATE") == 0) {
        if (!safe_for_manual_command()) return;
        uint8_t cmd = REG_SELF_CAL;
        if (send_register(&cmd, 1) != ESP_OK) {
            send_reply("ERR I2C_WRITE");
            return;
        }
        send_reply("OK CALIBRATE");
    } else {
        send_reply("ERR UNKNOWN_COMMAND");
    }
}

void app_main(void)
{
    usb_serial_jtag_driver_config_t usb_cfg = {
        .tx_buffer_size = 1024,
        .rx_buffer_size = 1024,
        .intr_priority = 0
    };
    ESP_ERROR_CHECK(usb_serial_jtag_driver_install(&usb_cfg));

    i2c_master_bus_config_t bus_cfg = {
        .clk_source = I2C_CLK_SRC_DEFAULT,
        .i2c_port = I2C_NUM_0,
        .sda_io_num = SDA_PIN,
        .scl_io_num = SCL_PIN,
        .glitch_ignore_cnt = 7,
        .flags.enable_internal_pullup = true
    };
    i2c_master_bus_handle_t bus;
    ESP_ERROR_CHECK(i2c_new_master_bus(&bus_cfg, &bus));
    i2c_device_config_t dev_cfg = {
        .dev_addr_length = I2C_ADDR_BIT_LEN_7,
        .device_address = FADER_ADDR,
        .scl_speed_hz = 100000
    };
    ESP_ERROR_CHECK(i2c_master_bus_add_device(bus, &dev_cfg, &fader));

    char input[96];
    size_t used = 0;
    for (;;) {
        char c;
        if (usb_serial_jtag_read_bytes(&c, 1, pdMS_TO_TICKS(100)) != 1) {
            continue;
        }
        if (c == '\r') continue;
        if (c == '\n') {
            input[used] = '\0';
            if (used) handle_command(input);
            used = 0;
        } else if (used < sizeof(input) - 1) {
            input[used++] = c;
        } else {
            used = 0;
            send_reply("ERR LINE_TOO_LONG");
        }
    }
}
