//+------------------------------------------------------------------+
//|                                              ReverseTrailing.mq5 |
//|                                                Senior Developer  |
//+------------------------------------------------------------------+
#property copyright "Senior MQL5 Developer"
#property link      ""
#property version   "1.00"
#property strict

// Включаем стандартную библиотеку для торговых операций
#include <Trade\Trade.mqh>
#include <Trade\SymbolInfo.mqh>

CTrade        m_trade;       // Объект для исполнения торговых приказов
CSymbolInfo   m_symbol;      // Объект для получения рыночных данных

//--- Перечисление для типа ММ
enum ENUM_MM_TYPE
  {
   MM_FIXED_LOT   = 0, // Фиксированный лот
   MM_PERCENT_DEP = 1  // % от свободного депозита
  };

//--- Входные параметры
input group "=== Торговые настройки ==="
input ENUM_MM_TYPE InpMMType       = MM_FIXED_LOT;   // Тип управления капиталом
input double       InpLotSize      = 0.1;            // Фиксированный лот (если выбран Fixed)
input double       InpRiskPercent  = 1.0;            // Процент риска от Free Margin (если выбран %)
input int          InpDistancePips = 100;            // Расстояние для Трейлинга и Стоп-ордеров (в пипсах/поинтах)
input ulong        InpMagicNumber  = 123456;         // Магический номер робота

//--- Глобальные переменные
double m_adjusted_distance = 0;

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
  {
   // Инициализируем данные по текущему символу
   if(!m_symbol.Name(_Symbol))
     {
      Print("Ошибка инициализации данных символа.");
      return(INIT_FAILED);
     }
     
   m_trade.SetExpertMagicNumber(InpMagicNumber);
   
   // Корректировка дистанции для инструментов с разным количеством знаков
   m_adjusted_distance = InpDistancePips * _Point;

   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
  }

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
  {
   // Обновляем котировки
   if(!m_symbol.RefreshRates()) return;

   // Проверяем наличие открытых позиций по нашему Magic
   bool has_position = false;
   ENUM_POSITION_TYPE pos_type = POSITION_TYPE_BUY;
   double pos_open = 0;
   double pos_sl = 0;
   ulong  pos_ticket = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber)
        {
         has_position = true;
         pos_type   = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
         pos_open   = PositionGetDouble(POSITION_PRICE_OPEN);
         pos_sl     = PositionGetDouble(POSITION_SL);
         pos_ticket = PositionGetInteger(POSITION_TICKET);
         break; // Работаем с одной основной позицией
        }
     }

   // Проверяем наличие отложенных ордеров по нашему Magic
   bool has_stop_order = false;
   ulong order_ticket = 0;
   ENUM_ORDER_TYPE ord_type = ORDER_TYPE_BUY_STOP;

   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket > 0)
        {
         if(OrderGetString(ORDER_SYMBOL) == _Symbol && OrderGetInteger(ORDER_MAGIC) == InpMagicNumber)
           {
            has_stop_order = true;
            order_ticket   = ticket;
            ord_type       = (ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
            break;
           }
        }
     }

   //--- ЛОГИКА 1: Старт системы (Нет позиций и нет ордеров)
   if(!has_position && !has_stop_order)
     {
      double lot = CalculateLot();
      // Открываем первую позицию по ТЗ (Бай позиция)
      if(m_trade.Buy(lot, _Symbol, m_symbol.Ask(), 0, 0, "Initial Buy"))
        {
         Print("Стартовая позиция BUY открыта.");
        }
      return;
     }

   //--- ЛОГИКА 2: Позиция закрылась, но отложенный ордер сработал (синхронный переворот)
   if(!has_position && has_stop_order)
     {
      // Если ордер еще висит, значит цена не дошла, либо мы в процессе обработки. 
      // Ждем активации ордера рынком.
      return;
     }

   //--- ЛОГИКА 3: Управление открытой позицией и синхронным Stop-ордером
   if(has_position)
     {
      double current_ask = m_symbol.Ask();
      double current_bid = m_symbol.Bid();
      double lot = CalculateLot();

      if(pos_type == POSITION_TYPE_BUY)
        {
         // Рассчитываем целевой уровень Трейлинг-Стопа для BUY (идет ЗА ценой Bid вниз)
         double target_sl = NormalizePrice(current_bid - m_adjusted_distance);
         
         // Трейлинг двигается ТОЛЬКО вверх за ценой
         if(pos_sl == 0 || target_sl > pos_sl)
           {
            // Корректируем/выставляем SL для позиции BUY и переставляем SELL STOP ордер
            if(target_sl > pos_open || pos_sl == 0) 
              {
               // Изменяем стоп-лосс позиции
               if(target_sl != pos_sl)
                 {
                  m_trade.PositionModify(pos_ticket, target_sl, 0);
                 }
                 
               // Управляем переворотным SELL_STOP ордером. Он должен стоять точно на уровне нашего SL.
               if(!has_stop_order)
                 {
                  m_trade.SellStop(lot, target_sl, _Symbol, 0, 0, ORDER_TIME_GTC, 0, "Reverse SellStop");
                 }
               else if(ord_type == ORDER_TYPE_SELL_STOP)
                 {
                  // Если ордер уже есть, двигаем его вслед за SL
                  if(NormalizePrice(OrderGetDouble(ORDER_PRICE_OPEN)) != target_sl)
                    {
                     m_trade.OrderModify(order_ticket, target_sl, 0, 0, ORDER_TIME_GTC, 0);
                    }
                 }
              }
           }
        }
      else if(pos_type == POSITION_TYPE_SELL)
        {
         // Рассчитываем целевой уровень Трейлинг-Стопа для SELL (идет ЗА ценой Ask вверх)
         double target_sl = NormalizePrice(current_ask + m_adjusted_distance);
         
         // Трейлинг двигается ТОЛЬКО вниз за ценой
         if(pos_sl == 0 || target_sl < pos_sl)
           {
            if(target_sl < pos_open || pos_sl == 0)
              {
               // Изменяем стоп-лосс позиции
               if(target_sl != pos_sl)
                 {
                  m_trade.PositionModify(pos_ticket, target_sl, 0);
                 }
                 
               // Управляем переворотным BUY_STOP ордером. Он стоит точно на уровне SL.
               if(!has_stop_order)
                 {
                  m_trade.BuyStop(lot, target_sl, _Symbol, 0, 0, ORDER_TIME_GTC, 0, "Reverse BuyStop");
                 }
               else if(ord_type == ORDER_TYPE_BUY_STOP)
                 {
                  // Двигаем ордер вслед за SL
                  if(NormalizePrice(OrderGetDouble(ORDER_PRICE_OPEN)) != target_sl)
                    {
                     m_trade.OrderModify(order_ticket, target_sl, 0, 0, ORDER_TIME_GTC, 0);
                    }
                 }
              }
           }
        }
     }
  }

//+------------------------------------------------------------------+
//| Расчет объема лота на основе мани-менеджмента                    |
//+------------------------------------------------------------------+
double CalculateLot()
  {
   if(InpMMType == MM_FIXED_LOT)
     {
      return InpLotSize;
     }
   else
     {
      double free_margin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
      double margin_for_lot = 0;
      
      // Получаем стоимость маржи для 1 лота
      if(!OrderCalcMargin(ORDER_TYPE_BUY, _Symbol, 1.0, m_symbol.Ask(), margin_for_lot) || margin_for_lot <= 0)
        {
         return InpLotSize; // Безопасный возврат дефолтного значения при ошибке
        }
        
      double calculated_lot = (free_margin * (InpRiskPercent / 100.0)) / margin_for_lot;
      
      // Округляем до шага лота инструмента
      double lot_step = m_symbol.LotsStep();
      calculated_lot = MathFloor(calculated_lot / lot_step) * lot_step;
      
      // Проверяем минимальные и максимальные лимиты брокера
      if(calculated_lot < m_symbol.LotsMin()) calculated_lot = m_symbol.LotsMin();
      if(calculated_lot > m_symbol.LotsMax()) calculated_lot = m_symbol.LotsMax();
      
      return calculated_lot;
     }
  }

//+------------------------------------------------------------------+
//| Нормализация цены под требования торгового сервера               |
//+------------------------------------------------------------------+
double NormalizePrice(double price)
  {
   double tick_size = m_symbol.TickSize();
   if(tick_size == 0) return price;
   return MathRound(price / tick_size) * tick_size;
  }