package main

import (
	"bytes"
	"fmt"
	"net"
	"os"
	"sync"
	"time"

	"github.com/gorilla/websocket"
)

const (
	ClientsToWaitFor     = 100
	Delay                = 64 * time.Millisecond
	WaitTimeBetweenTests = 5 * time.Second
)

var (
	logMessages    = os.Getenv("LOG_MESSAGES") == "1"
	messagesToSend = []string{"Hello World!", "Hello World! 1", "What is the meaning of life?"}
	names          = generateNames(ClientsToWaitFor)
	results        = make([]Result, 0)
	mu             sync.Mutex // For synchronizing results
)

type Server struct {
	Name     string
	URL      string
	Protocol string
}

type Result struct {
	Name        string  `json:"name"`
	Average     float64 `json:"average"`
	LostPackets int     `json:"lost_packets"`
	Percentage  float64 `json:"percentage"`
}

func main() {
	servers := []Server{
		{Name: "C# WebSocket", URL: "ws://127.0.0.1:3001", Protocol: "ws"},
		{Name: "C# TCP", URL: "127.0.0.1:4001", Protocol: "tcp"},
		{Name: "C# UDP", URL: "127.0.0.1:5001", Protocol: "udp"},
	}

	for _, server := range servers {
		testServer(server)
		time.Sleep(WaitTimeBetweenTests)
	}

	calculateResults()
}

func testServer(server Server) {
	fmt.Printf("Connecting to %s at %s\n", server.Name, server.URL)
	var received int
	var lostPackets int
	var clients []net.Conn
	var wsClients []*websocket.Conn

	switch server.Protocol {
	case "ws":
		for i := 0; i < ClientsToWaitFor; i++ {
			conn, _, err := websocket.DefaultDialer.Dial(server.URL, nil)
			if err != nil {
				fmt.Printf("Failed to connect to WebSocket server: %v\n", err)
				continue
			}
			wsClients = append(wsClients, conn)
			go handleWSMessages(conn, &received)
		}

	case "tcp":
		for i := 0; i < ClientsToWaitFor; i++ {
			conn, err := net.Dial("tcp", server.URL)
			if err != nil {
				fmt.Printf("Failed to connect to TCP server: %v\n", err)
				continue
			}
			clients = append(clients, conn)
			go handleTCPMessages(conn, &received)
		}

	case "udp":
		for i := 0; i < ClientsToWaitFor; i++ {
			conn, err := net.Dial("udp", server.URL)
			if err != nil {
				fmt.Printf("Failed to connect to UDP server: %v\n", err)
				continue
			}
			clients = append(clients, conn)
			go handleUDPMessages(conn, &received)
		}
	}

	// Chamando a função com os parâmetros corretos
	go sendMessagesContinuously(server, clients, wsClients, &lostPackets)

	// Coletando os resultados
	time.Sleep(10 * time.Second)
	fmt.Printf("%s: %d messages received\n", server.Name, received)

	// Fechando conexões
	for _, client := range clients {
		client.Close()
	}
	for _, ws := range wsClients {
		ws.Close()
	}

	results = append(results, Result{Name: server.Name, Average: float64(received), LostPackets: lostPackets})
}

func handleWSMessages(conn *websocket.Conn, received *int) {
	for {
		_, message, err := conn.ReadMessage()
		if err != nil {
			break
		}
		if logMessages {
			fmt.Println("WS received:", string(message))
		}
		mu.Lock()
		*received++
		mu.Unlock()
	}
}

func handleTCPMessages(conn net.Conn, received *int) {
	buffer := make([]byte, 1024)
	for {
		n, err := conn.Read(buffer)
		if err != nil {
			break
		}
		if logMessages {
			fmt.Println("TCP received:", string(buffer[:n]))
		}
		mu.Lock()
		*received++
		mu.Unlock()
	}
}

func handleUDPMessages(conn net.Conn, received *int) {
	buffer := make([]byte, 1024)
	for {
		n, err := conn.Read(buffer)
		if err != nil {
			break
		}
		if logMessages {
			fmt.Println("UDP received:", string(buffer[:n]))
		}
		mu.Lock()
		*received++
		mu.Unlock()
	}
}

func sendMessagesContinuously(server Server, clients []net.Conn, wsClients []*websocket.Conn, lostPackets *int) {
	ticker := time.NewTicker(Delay)
	defer ticker.Stop()

	for range ticker.C {
		for i := 0; i < ClientsToWaitFor; i++ {
			for _, msg := range messagesToSend {
				switch server.Protocol {
				case "ws":
					if wsClients[i] != nil {
						err := wsClients[i].WriteMessage(websocket.TextMessage, []byte(msg))
						if err != nil {
							mu.Lock()
							*lostPackets++
							mu.Unlock()
						}
					}
				case "tcp", "udp":
					if clients[i] != nil {
						_, err := clients[i].Write([]byte(msg))
						if err != nil {
							mu.Lock()
							*lostPackets++
							mu.Unlock()
						}
					}
				}
			}
		}
	}
}

func generateNames(n int) []string {
	names := make([]string, n)
	for i := 0; i < n; i++ {
		names[i] = fmt.Sprintf("Client%d", i)
	}
	return names
}

func calculateResults() {
	var overallAverage float64
	for _, result := range results {
		overallAverage += result.Average
	}
	overallAverage /= float64(len(results))

	for i := range results {
		results[i].Percentage = ((results[i].Average - overallAverage) / overallAverage) * 100
	}

	// Sort results by average messages per second
	for i := range results {
		for j := i + 1; j < len(results); j++ {
			if results[j].Average > results[i].Average {
				results[i], results[j] = results[j], results[i]
			}
		}
	}

	printResults(results)
}

func printResults(results []Result) {
	buffer := new(bytes.Buffer)
	for _, result := range results {
		fmt.Fprintf(buffer, "Server: %s\nAvg Messages/sec: %.2f\nLost Packets: %d\nPercentage Difference: %.2f%%\n\n",
			result.Name, result.Average, result.LostPackets, result.Percentage)
	}
	fmt.Println(buffer.String())
}
