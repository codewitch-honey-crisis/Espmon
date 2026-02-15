/*
 * SPDX-FileCopyrightText: 2025 CFSoft Systems (Chengdu) CO LTD
 *
 * SPDX-License-Identifier: Apache-2.0
 */

#include <stdio.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_system.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_check.h"
#include "driver/gpio.h"
#include "esp_lcd_panel_io.h"
#include "esp_lcd_touch.h"

static const char *TAG = "FT6x36";

/* Registers */
#define FT62XX_G_FT5201ID 0xA8     // FocalTech's panel ID
#define FT62XX_REG_NUMTOUCHES 0x02 // Number of touch points

#define FT62XX_NUM_X 0x33 // Touch X position
#define FT62XX_NUM_Y 0x34 // Touch Y position

#define FT62XX_REG_MODE 0x00        // Device mode, either WORKING or FACTORY
#define FT62XX_REG_READDATA 0x00    // Read data from register
#define FT62XX_REG_CALIBRATE 0x02   // Calibrate mode
#define FT62XX_REG_WORKMODE 0x00    // Work mode
#define FT62XX_REG_FACTORYMODE 0x40 // Factory mode
#define FT62XX_REG_THRESHHOLD 0x80  // Threshold for touch detection
#define FT62XX_REG_POINTRATE 0x88   // Point rate
#define FT62XX_REG_FIRMVERS 0xA6    // Firmware version
#define FT62XX_REG_CHIPID 0xA3      // Chip selecting
#define FT62XX_REG_VENDID 0xA8      // FocalTech's panel ID

#define FT62XX_VENDID 0x11  // FocalTech's panel ID
#define FT6206_CHIPID 0x06  // Chip selecting
#define FT3236_CHIPID 0x33  // Chip selecting
#define FT6236_CHIPID 0x36  // Chip selecting
#define FT6236U_CHIPID 0x64 // Chip selecting
#define FT6336U_CHIPID 0x64 // Chip selecting

#define FT62XX_DEFAULT_THRESHOLD 128 // Default threshold for touch detection

/*******************************************************************************
 * Function definitions
 *******************************************************************************/
static esp_err_t esp_lcd_touch_ft6x36_read_data(esp_lcd_touch_handle_t tp);
static bool esp_lcd_touch_ft6x36_get_xy(esp_lcd_touch_handle_t tp, uint16_t *x, uint16_t *y, uint16_t *strength, uint8_t *point_num, uint8_t max_point_num);
static esp_err_t esp_lcd_touch_ft6x36_del(esp_lcd_touch_handle_t tp);

/* I2C read */
static esp_err_t touch_ft6x36_i2c_write(esp_lcd_touch_handle_t tp, uint8_t reg, uint8_t data);
static esp_err_t touch_ft6x36_i2c_read(esp_lcd_touch_handle_t tp, uint8_t reg, uint8_t *data, uint8_t len);

/* FT6x36 init */
static esp_err_t touch_ft6x36_init(esp_lcd_touch_handle_t tp);
/* FT6x36 reset */
static esp_err_t touch_ft6x36_reset(esp_lcd_touch_handle_t tp);

/*******************************************************************************
 * Public API functions
 *******************************************************************************/

esp_err_t esp_lcd_touch_new_i2c_ft6x36(const esp_lcd_panel_io_handle_t io, const esp_lcd_touch_config_t *config, esp_lcd_touch_handle_t *out_touch)
{
    esp_err_t ret = ESP_OK;

    assert(config != NULL);
    assert(out_touch != NULL);

    /* Prepare main structure */
    esp_lcd_touch_handle_t esp_lcd_touch_ft6x36 = heap_caps_calloc(1, sizeof(esp_lcd_touch_t), MALLOC_CAP_DEFAULT);
    ESP_GOTO_ON_FALSE(esp_lcd_touch_ft6x36, ESP_ERR_NO_MEM, err, TAG, "no mem for FT6x36 controller");

    /* Communication interface */
    esp_lcd_touch_ft6x36->io = io;

    /* Only supported callbacks are set */
    esp_lcd_touch_ft6x36->read_data = esp_lcd_touch_ft6x36_read_data;
    esp_lcd_touch_ft6x36->get_xy = esp_lcd_touch_ft6x36_get_xy;
    esp_lcd_touch_ft6x36->del = esp_lcd_touch_ft6x36_del;

    /* Mutex */
    esp_lcd_touch_ft6x36->data.lock.owner = portMUX_FREE_VAL;

    /* Save config */
    memcpy(&esp_lcd_touch_ft6x36->config, config, sizeof(esp_lcd_touch_config_t));

    /* Prepare pin for touch interrupt */
    if (esp_lcd_touch_ft6x36->config.int_gpio_num != GPIO_NUM_NC)
    {
        const gpio_config_t int_gpio_config = {
            .mode = GPIO_MODE_INPUT,
            .intr_type = (esp_lcd_touch_ft6x36->config.levels.interrupt ? GPIO_INTR_POSEDGE : GPIO_INTR_NEGEDGE),
            .pin_bit_mask = BIT64(esp_lcd_touch_ft6x36->config.int_gpio_num)};
        ret = gpio_config(&int_gpio_config);
        ESP_GOTO_ON_ERROR(ret, err, TAG, "GPIO config failed");

        /* Register interrupt callback */
        if (esp_lcd_touch_ft6x36->config.interrupt_callback)
        {
            esp_lcd_touch_register_interrupt_callback(esp_lcd_touch_ft6x36, esp_lcd_touch_ft6x36->config.interrupt_callback);
        }
    }

    /* Prepare pin for touch controller reset */
    if (esp_lcd_touch_ft6x36->config.rst_gpio_num != GPIO_NUM_NC)
    {
        const gpio_config_t rst_gpio_config = {
            .mode = GPIO_MODE_OUTPUT,
            .pin_bit_mask = BIT64(esp_lcd_touch_ft6x36->config.rst_gpio_num)};
        ret = gpio_config(&rst_gpio_config);
        ESP_GOTO_ON_ERROR(ret, err, TAG, "GPIO config failed");
    }

    /* Reset controller */
    ret = touch_ft6x36_reset(esp_lcd_touch_ft6x36);
    ESP_GOTO_ON_ERROR(ret, err, TAG, "FT6x36 reset failed");

    /* Init controller */
    ret = touch_ft6x36_init(esp_lcd_touch_ft6x36);
    ESP_GOTO_ON_ERROR(ret, err, TAG, "FT6x36 init failed");

    *out_touch = esp_lcd_touch_ft6x36;

err:
    if (ret != ESP_OK)
    {
        ESP_LOGE(TAG, "Error (0x%x)! Touch controller FT6x36 initialization failed!", ret);
        if (esp_lcd_touch_ft6x36)
        {
            esp_lcd_touch_ft6x36_del(esp_lcd_touch_ft6x36);
        }
    }

    return ret;
}

static esp_err_t esp_lcd_touch_ft6x36_read_data(esp_lcd_touch_handle_t tp)
{
    esp_err_t err;
    uint8_t data[16];
    uint8_t points, touches;
    size_t i = 0;

    assert(tp != NULL);

    /* Read number of touched points */
    err = touch_ft6x36_i2c_read(tp, FT62XX_REG_NUMTOUCHES, &points, 1);
    ESP_RETURN_ON_ERROR(err, TAG, "I2C read error!");

    if (points > 2 || points == 0)
    {
        return ESP_OK;
    }

    /* Number of touched points */
    points = (points > CONFIG_ESP_LCD_TOUCH_MAX_POINTS ? CONFIG_ESP_LCD_TOUCH_MAX_POINTS : points);

    err = touch_ft6x36_i2c_read(tp, FT62XX_REG_READDATA, data, 16);
    ESP_RETURN_ON_ERROR(err, TAG, "I2C read error!");

    /* Number of touched points */
    touches = data[0x02];

    /* Check if the number of touched points is correct */
    if (points != touches)
    {
        return ESP_OK;
    }

    portENTER_CRITICAL(&tp->data.lock);

    /* Number of touched points */
    tp->data.points = points;

    /* Fill all coordinates */
    for (i = 0; i < points; i++)
    {
        tp->data.coords[i].x = ((data[0x03 + i * 6] & 0x0F) << 8) | data[0x04 + i * 6];
        tp->data.coords[i].y = ((data[0x05 + i * 6] & 0x0F) << 8) | data[0x06 + i * 6];
    }

    portEXIT_CRITICAL(&tp->data.lock);

    return ESP_OK;
}

static bool esp_lcd_touch_ft6x36_get_xy(esp_lcd_touch_handle_t tp, uint16_t *x, uint16_t *y, uint16_t *strength, uint8_t *point_num, uint8_t max_point_num)
{
    assert(tp != NULL);
    assert(x != NULL);
    assert(y != NULL);
    assert(point_num != NULL);
    assert(max_point_num > 0);

    portENTER_CRITICAL(&tp->data.lock);

    /* Count of points */
    *point_num = (tp->data.points > max_point_num ? max_point_num : tp->data.points);

    for (size_t i = 0; i < *point_num; i++)
    {
        x[i] = tp->data.coords[i].x;
        y[i] = tp->data.coords[i].y;

        if (strength)
        {
            strength[i] = tp->data.coords[i].strength;
        }
    }

    /* Invalidate */
    tp->data.points = 0;

    portEXIT_CRITICAL(&tp->data.lock);

    return (*point_num > 0);
}

static esp_err_t esp_lcd_touch_ft6x36_del(esp_lcd_touch_handle_t tp)
{
    assert(tp != NULL);

    /* Reset GPIO pin settings */
    if (tp->config.int_gpio_num != GPIO_NUM_NC)
    {
        gpio_reset_pin(tp->config.int_gpio_num);
        if (tp->config.interrupt_callback)
        {
            gpio_isr_handler_remove(tp->config.int_gpio_num);
        }
    }

    /* Reset GPIO pin settings */
    if (tp->config.rst_gpio_num != GPIO_NUM_NC)
    {
        gpio_reset_pin(tp->config.rst_gpio_num);
    }

    free(tp);

    return ESP_OK;
}

/*******************************************************************************
 * Private API function
 *******************************************************************************/

static esp_err_t touch_ft6x36_init(esp_lcd_touch_handle_t tp)
{
    esp_err_t ret = ESP_OK;
    uint8_t vend_id, chip_id, firm_vers, point_rate, thresh, reg_val;

    assert(tp != NULL);

    /* Read vendor ID */
    ESP_RETURN_ON_ERROR(touch_ft6x36_i2c_read(tp, FT62XX_REG_VENDID, &vend_id, 1), TAG, "Read vendor ID error");

    /* Read chip ID */
    ESP_RETURN_ON_ERROR(touch_ft6x36_i2c_read(tp, FT62XX_REG_CHIPID, &chip_id, 1), TAG, "Read chip ID error");

    /* Read firmware version */
    ESP_RETURN_ON_ERROR(touch_ft6x36_i2c_read(tp, FT62XX_REG_FIRMVERS, &firm_vers, 1), TAG, "Read firmware version error");

    /* Read point rate */
    ESP_RETURN_ON_ERROR(touch_ft6x36_i2c_read(tp, FT62XX_REG_POINTRATE, &point_rate, 1), TAG, "Read point rate error");

    /* Read threshold */
    ESP_RETURN_ON_ERROR(touch_ft6x36_i2c_read(tp, FT62XX_REG_THRESHHOLD, &thresh, 1), TAG, "Read threshold error");

    /* Print out the values */
    ESP_LOGI(TAG, "Vend ID: 0x%02X", vend_id);
    ESP_LOGI(TAG, "Chip ID: 0x%02X", chip_id);
    ESP_LOGI(TAG, "Firm V: %d", firm_vers);
    ESP_LOGI(TAG, "Point Rate Hz: %d", point_rate);
    ESP_LOGI(TAG, "Thresh: %d", thresh);

    /* Dump all registers */
    ESP_LOGI(TAG, "Dumping all registers:");
    for (int16_t i = 0; i < 0x10; i++)
    {
        /* Read register */
        if (touch_ft6x36_i2c_read(tp, i, &reg_val, 1) == ESP_OK)
        {
            ESP_LOGI(TAG, "I2C $%02X = 0x%02X", i, reg_val);
        }
        else
        {
            ESP_LOGE(TAG, "Failed to read register 0x%02X", i);
        }
    }

    /* Check if the values are valid */
    if (vend_id != FT62XX_VENDID)
    {
        ESP_LOGE(TAG, "Invalid vendor ID: 0x%02X", vend_id);
        return ESP_FAIL;
    }

    /* Check if the chip ID is supported */
    if (chip_id != FT6206_CHIPID && chip_id != FT6236_CHIPID && chip_id != FT6236U_CHIPID && chip_id != FT6336U_CHIPID && chip_id != FT3236_CHIPID)
    {
        ESP_LOGE(TAG, "Unsupported chip ID: 0x%02X", chip_id);
        return ESP_FAIL;
    }

    /* Set to work mode */
    ret |= touch_ft6x36_i2c_write(tp, FT62XX_REG_MODE, FT62XX_REG_WORKMODE);

    /* Set threshold */
    ret |= touch_ft6x36_i2c_write(tp, FT62XX_REG_THRESHHOLD, FT62XX_DEFAULT_THRESHOLD);

    /* Set point rate */
    ret |= touch_ft6x36_i2c_write(tp, FT62XX_REG_POINTRATE, 0x0E);

    return ret;
}

/* Reset controller */
static esp_err_t touch_ft6x36_reset(esp_lcd_touch_handle_t tp)
{
    assert(tp != NULL);

    if (tp->config.rst_gpio_num != GPIO_NUM_NC)
    {
        ESP_RETURN_ON_ERROR(gpio_set_level(tp->config.rst_gpio_num, tp->config.levels.reset), TAG, "GPIO set level error!");
        vTaskDelay(pdMS_TO_TICKS(10));
        ESP_RETURN_ON_ERROR(gpio_set_level(tp->config.rst_gpio_num, !tp->config.levels.reset), TAG, "GPIO set level error!");
        vTaskDelay(pdMS_TO_TICKS(10));
    }

    return ESP_OK;
}

static esp_err_t touch_ft6x36_i2c_write(esp_lcd_touch_handle_t tp, uint8_t reg, uint8_t data)
{
    assert(tp != NULL);

    // *INDENT-OFF*
    /* Write data */
    return esp_lcd_panel_io_tx_param(tp->io, reg, (uint8_t[]){data}, 1);
    // *INDENT-ON*
}

static esp_err_t touch_ft6x36_i2c_read(esp_lcd_touch_handle_t tp, uint8_t reg, uint8_t *data, uint8_t len)
{
    assert(tp != NULL);
    assert(data != NULL);

    /* Read data */
    return esp_lcd_panel_io_rx_param(tp->io, reg, data, len);
}
