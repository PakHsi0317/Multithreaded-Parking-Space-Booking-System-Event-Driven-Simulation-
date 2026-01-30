using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Project2
{
    //create delegate to subscribe and follow the correspond functonality in the system wide
    public delegate void newProcessOrderEvent();
    public delegate void processOrderEvent(string senderId, int cardNo, int amount, double price);
    public delegate void priceCutEvent(string senderId, double price);
    public delegate void priceChangeEvent(string senderId, double price);

    class Program {
        public static Thread[] AgentT = new Thread[5];
        //variable for our MultiCellBuffer
        public static MultiCellBuffer MultiCellBuffer;

        static void Main(string[] args) {

            // Create an array of ParkingStructures
            ParkingStructure[] parkingStructures = new ParkingStructure[3];

            // Create an array of ParkAgents
            ParkAgent[] parkAgents = new ParkAgent[3];

            // Initialize the arrays
            for (int i = 0; i < 3; i++) {
                parkingStructures[i] = new ParkingStructure();
                parkAgents[i] = new ParkAgent();
            }

            // instantiate a 3 cells MultiCellBuffer
            MultiCellBuffer = new MultiCellBuffer();

            Thread parkThread = new Thread(new ThreadStart(parkingStructures[0].Run));
            parkThread.Start();

            // use event handler when a ticketSale occurs
            ParkingStructure.priceCut += new priceCutEvent(parkAgents[0].priceCutActivity);
            ParkAgent.lOrder += new newProcessOrderEvent(parkingStructures[1].receive);

            // callback when order is processed
            OrderProcessing.process += new processOrderEvent(parkAgents[1].orderProcess);

            //use when price increase 
            ParkingStructure.priceChange += new priceChangeEvent(parkAgents[2].priceIn);
            ParkAgent.nOrder += new newProcessOrderEvent(parkingStructures[2].receive);

            for (int i = 0; i < 5; i++) {
                AgentT[i] = new Thread(new ThreadStart(parkAgents[0].Agent));
                AgentT[i].Name = (i + 1).ToString();
                AgentT[i].Start();
            }
        }
    }

    class ParkingStructure {
        public static event priceCutEvent priceCut;
        static Random random = new Random();
        public static event priceChangeEvent priceChange;
        private static double price = 100;
        private int counter = 0;
        private const int maxCounter = 20;

        public void Run() {
            // stop before counter hit 20
            while (counter < maxCounter) {
                //get new price every 1 seconds
                Thread.Sleep(1000);
                //set the price model
                price = PricingModel();
                double change = (random.NextDouble() * 0.20 - 0.10) * price;

                // Update the price with the percentage change, but keep it within the range [10, 40]
                price = Math.Max(10, Math.Min(40, price + change));
                counter++;

            }
        }

        //generate a random price
        public double PricingModel() {
            price = random.NextDouble() * 30 + 10;
            return price;
        }

        //This method will receive order from MultiCellBuffer
        public void receive() {
            // retrieves Purchase Order from MultiCellBuffer
            OrderClass purchaseOrder = Program.MultiCellBuffer.GetOneCell();
            Thread thread = new Thread(() => OrderProcessing.orderProcess(purchaseOrder, price));
            thread.Start();
        }
    }

    class OrderProcessing {
        private const int MinCreditCardNumber = 5000;
        private const int MaxCreditCardNumber = 7000;
        public static event processOrderEvent process;

        //Check if the card is invalid or not
        public static void orderProcess(OrderClass order, double price) {
            if (isCreditCardValid(order.CardNo)) {
                //then we process the payment
                double cost = (price * order.Quantity);
                // insert processPO event here
                process(order.SenderId, order.CardNo, order.Quantity, cost);   // emit our processPO event
            }
            else {
                Console.WriteLine(" Your card is invalid: " + order.CardNo);
                return;

            }
        }
        //The card is invalid it's lower than 4000 or higher than 5000
        private static Boolean isCreditCardValid(int cardNumber)
        {
            return cardNumber >= MinCreditCardNumber && cardNumber <= MaxCreditCardNumber;
        }
    }
    class MultiCellBuffer
    {
        private static Semaphore emptySlots = new Semaphore(SIZE, SIZE);
        private static Semaphore filledSlots = new Semaphore(SIZE, SIZE);
        private const int SIZE = 3;
        private int n = 0;
        private static Semaphore[] locks; // set a lock to make synchronous
        private int head = 0, tail = 0;
        // instantiate orderList in MultiCellBuffer
        List<OrderClass> orderlist = new List<OrderClass>(SIZE);

        public OrderClass GetOneCell() {

            OrderClass order = new OrderClass();
            emptySlots.WaitOne();
            head = (head + 1) % SIZE; // move head

            lock (orderlist) {
                //if our cells are empty, wait
                if (n == 0) {
                    Monitor.Wait(orderlist);
                }
                // read buffer cells
                for (int i = 0; i < SIZE; i++) {
                    if (orderlist[i] != null) {
                        //get the order from the buffer cell
                        order = orderlist[i];
                        //just clear the buffer cell
                        orderlist.RemoveAt(i);
                        n--;
                        break;
                    }
                }
                emptySlots.Release();
                Monitor.Pulse(orderlist);
            }
            return order;
        }

        //locking oderlist, and read/write permissions, prevent over the nums
        public void SetOneCell(OrderClass order) {
            //requesting cell 
            filledSlots.WaitOne();
            lock (orderlist) {
                if (n == SIZE) {
                    // block other threads from trying to send a new PO to MCB while PO count = 3
                    Monitor.Wait(orderlist);
                }

                // for loop to insert our purchase order into the next available cell in our list
                for (int i = 0; i < SIZE; i++) {
                    // orderlist.Insert(i, order);
                    //restore the order made
                    orderlist.Add(order);
                    tail = (tail + 1) % SIZE; // move tail
                    n++;
                    i = SIZE;
                }
                // release the semaphore
                filledSlots.Release();
                // send notification to thread and run aganin
                Monitor.Pulse(orderlist);
            }
        }
    }

    class ParkAgent
    {
        // get random number 
        Random random = new Random();

        public static event newProcessOrderEvent nOrder;
        public static event newProcessOrderEvent lOrder;

        //use to determine when to end thread
        public void Agent()
        {
            while (true)
            {
                Thread.Sleep(1500);
                priceOrder(Thread.CurrentThread.Name, 100);
            }
        }

        //ticketsale is used to create order and send to MultiCellBuffer
        public void priceCutActivity(string senderId, double unitPrice)
        {
            // create a purchase order for tickets from our park and place in queue.    
            Console.WriteLine(" Price Cut!!! ${0}", unitPrice);
            priceOrder(senderId, unitPrice);
        }

        public void orderProcess(string senderId, int cardNo, int amount, double unitPrice)
        {
            double costPerOne = unitPrice / amount;
            Console.WriteLine("Agent {0}: you have ordered  " + amount + " parking spots. " +
            costPerOne + "$ per one, the \ntotal amount is " + unitPrice + "$. We charged Credit card: {1}\n", senderId, cardNo);
        }

        private void priceOrder(string threadID, double unitPrice)
        {
            int cardNo = random.Next(5000, 7000);
            int amount = random.Next(30, 100);
            // to hold our ticket purchase order
            OrderClass order = new OrderClass(threadID, cardNo, amount, unitPrice);

            //start timestamp before sending purchase order 
            Console.WriteLine("The number {0} Agent has a new order for {1} parking spot at {2}.", threadID, amount, DateTime.Now.ToString("hh:mm:ss"));
            Program.MultiCellBuffer.SetOneCell(order);

            // emits event
            nOrder();
        }

        public void priceIn(string senderId, double unitPrice) {
            int cardNo = random.Next(5000, 7000);
            int amount = random.Next(10, 40);
            Console.WriteLine("CUT ENDS! New price is ${0}.\n", unitPrice);
            // instantiate and OrderClass object to hold our ticket purchase order
            OrderClass order = new OrderClass(senderId, cardNo, amount, unitPrice);

            // implementation of timestamp before sending purchase order to MultiCellBuffer.
            Console.WriteLine("Ticket Agency number {0} has a new order for {1} tickets at {2}.", senderId, amount, DateTime.Now.ToString("hh:mm:ss"));
            Program.MultiCellBuffer.SetOneCell(order);
            // emits event
            nOrder();
        }
    }

    class OrderClass
    {
        // declare the attributes
        private string senderId, receiverID;
        private int cardNo, quantity;
        private double unitPrice;
        public OrderClass() {}
        public OrderClass(string senderId, int cardNo, int quantity, double unitPrice) {
            this.senderId = senderId;
            this.cardNo = cardNo;
            this.quantity = quantity;
            this.unitPrice = unitPrice;
        }
        //create getter and setter and put a lock to make sychnrous
        public string SenderId {
            get { lock (this) { return senderId; } }
            set { lock (this) { senderId = value; } }
        }

        public int CardNo {
            get { lock (this) { return cardNo; } }
            set { lock (this) { cardNo = value; } }
        }

        public string ReceiverID {
            get { lock (this) { return receiverID; } }
            set { lock (this) { receiverID = value; } }
        }

        public int Quantity {
            get { lock (this) { return quantity; } }
            set { lock (this) { quantity = value; } }
        }

        public double UnitPrice {
            get { lock (this) { return unitPrice; } }
            set { lock (this) { unitPrice = value; } }
        }
    }
}
